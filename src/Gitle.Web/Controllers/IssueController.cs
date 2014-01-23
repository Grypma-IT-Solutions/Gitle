﻿namespace Gitle.Web.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Admin;
    using Clients.GitHub.Interfaces;
    using Clients.GitHub.Models;
    using Model;
    using Model.Helpers;
    using Model.Interfaces.Repository;
    using Helpers;
    using Label = Clients.GitHub.Models.Label;

    public class IssueController : SecureController
    {
        private readonly IIssueClient client;
        private readonly ICommentClient commentClient;
        private readonly IProjectRepository repository;
        private readonly IMilestoneClient milestoneClient;
        private readonly ILabelClient labelClient;

        public IssueController(IProjectRepository repository, IIssueClient client, ICommentClient commentClient, IMilestoneClient milestoneClient, ILabelClient labelClient)
        {
            this.repository = repository;
            this.client = client;
            this.commentClient = commentClient;
            this.milestoneClient = milestoneClient;
            this.labelClient = labelClient;
        }

        [MustHaveProject]
        public void Index(string projectSlug, string label, string state)
        {
            var project = repository.FindBySlug(projectSlug);

            var items = client.List(project.Repository, project.MilestoneId, string.IsNullOrEmpty(state) ? "open,closed" : state);

            if (!string.IsNullOrEmpty(label) && label != "0")
                items = items.Where(i => i.Labels.Any(l => l.Name == label)).ToList();

            PropertyBag.Add("items", items);
            PropertyBag.Add("project", project);
            PropertyBag.Add("label", label);
            PropertyBag.Add("labels", CurrentUser.IsAdmin ? project.Labels : project.Labels.Where(l => l.VisibleForCustomer));
            PropertyBag.Add("state", state);
        }

        [MustHaveProject]
        public void View(string projectSlug, int issueId)
        {
            var project = repository.FindBySlug(projectSlug);
            PropertyBag.Add("project", project);
            var item = client.Get(project.Repository, issueId);
            PropertyBag.Add("item", item);
            var comments = commentClient.List(project.Repository, issueId);
            PropertyBag.Add("comments", comments);
        }

        [MustHaveProject]
        public void New(string projectSlug)
        {
            PropertyBag.Add("project", repository.FindBySlug(projectSlug));
            PropertyBag.Add("item", new Issue());
            RenderView("edit");
        }

        [Admin]
        public void Edit(string projectSlug, int issueId)
        {
            var project = repository.FindBySlug(projectSlug);
            PropertyBag.Add("project", project);
            var item = client.Get(project.Repository, issueId);
            PropertyBag.Add("item", item);
        }

        [MustHaveProject]
        public void Save(string projectSlug, int issueId, string[] labels)
        {
            var project = repository.FindBySlug(projectSlug);
            var issue = client.Get(project.Repository, issueId);

            if (issue != null)
            {
                BindObjectInstance(issue, "item");
            }
            else
            {
                issue = BindObject<Issue>("item");
            }

            issue.Labels = labels.Select(label => labelClient.Get(project.Repository, label)).ToList();

            issue.Milestone = milestoneClient.Get(project.Repository, project.MilestoneId);

            if (issue.Number > 0)
            {
                client.Patch(project.Repository, issue.Number, issue);
            }
            else
            {
                client.Post(project.Repository, issue);
            }
            RedirectToUrl(string.Format("/project/{0}/issue/index", project.Slug));
        }

        [MustHaveProject]
        public void AddComment(string projectSlug, int issueId, string body)
        {
            var project = repository.FindBySlug(projectSlug);
            var comment = new Comment
                              {
                                  Body = string.Format("({0}): {1}", CurrentUser.FullName, body)
                              };
            commentClient.Post(project.Repository, issueId, comment);
            RedirectToReferrer();
        }

        [Admin]
        public void Export(string projectSlug)
        {
            var project = repository.FindBySlug(projectSlug);
            var issues = client.List(project.Repository, project.MilestoneId);

            var csv = CsvHelper.IssuesCsv(project, issues);
            CancelView();

            Response.ClearContent();
            Response.Clear();

            var filename = string.Format("issues_{0}_{1:yyyyMMdd_hhmm}.csv", project.Name, DateTime.Now);

            Response.AppendHeader("content-disposition", string.Format("attachment; filename={0}", filename));
            Response.ContentType = "application/csv";

            var byteArray = Encoding.Default.GetBytes(csv);
            var stream = new MemoryStream(byteArray);
            Response.BinaryWrite(stream);
        }
    }
}