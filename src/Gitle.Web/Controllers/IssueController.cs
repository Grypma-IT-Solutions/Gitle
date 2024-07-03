namespace Gitle.Web.Controllers
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Castle.MonoRail.Framework;
    using Castle.MonoRail.Framework.Routing;
    using Clients.Freckle.Interfaces;
    using FluentNHibernate.Conventions;
    using Helpers;
    using Localization;
    using Model;
    using Model.Enum;
    using Model.Helpers;
    using Model.Interfaces.Service;
    using NHibernate;
    using Newtonsoft.Json;
    using QueryParsers;
    using Issue = Model.Issue;
    using Project = Model.Project;

    public class IssueController : SecureController
    {
        protected ISettingService SettingService { get; }

        private readonly ISessionFactory sessionFactory;
        private readonly IEmailService emailService;

        public IssueController(ISessionFactory sessionFactory, ISettingService settingService, IEmailService emailService) : base(sessionFactory)
        {
            SettingService = settingService;
            this.sessionFactory = sessionFactory;
            this.emailService = emailService;
        }

        [MustHaveProject]
        public void Index(string projectSlug, string query)
        {
            var project = session.SlugOrDefault<Project>(projectSlug);

            var parser = new IssueQueryParser(sessionFactory, query, project, CurrentUser);

            var filterPresets = session.Query<FilterPreset>().Where(x => x.User == CurrentUser && x.IsActive && (x.Project == null || (x.Project != null && x.Project.Id == project.Id))).ToList();
            var globalFilterPresets = session.Query<FilterPreset>().Where(x => x.User == null && x.IsActive && (x.Project == null || (x.Project != null && x.Project.Id == project.Id))).ToList();
            const string redHex = "fe0000";
            var labels = session.Query<Label>().Where(x => x.IsActive).ToArray();
            var hasRedLabel = false;

            foreach (var check in labels)
            {
                if (check.Color == redHex)
                {
                    hasRedLabel = true;
                }
            }

            PropertyBag.Add("project", project);
            PropertyBag.Add("result", parser);
            PropertyBag.Add("labels", CurrentUser.IsAdmin ? project.Labels : project.Labels.Where(l => l.VisibleForCustomer).ToList());
            PropertyBag.Add("redLabel", hasRedLabel);
            PropertyBag.Add("customerLabels", CurrentUser.IsAdmin ? project.Labels : project.Labels.Where(l => l.ApplicableByCustomer).ToList());
            PropertyBag.Add("filterPresets", filterPresets);
            PropertyBag.Add("globalFilterPresets", globalFilterPresets);
            PropertyBag.Add("allAdmins", session.Query<User>().Where(x => x.IsAdmin && x.IsActive).ToList());
            PropertyBag.Add("selectedPickupbys", parser.SelectedPickupbys);
            PropertyBag.Add("pickupany", parser.PickupAny);
            PropertyBag.Add("pickupnone", parser.PickupNone);
            PropertyBag.Add("dump", CreateDummyIssue(project));
            PropertyBag.Add("isSubscribed", CurrentUser.Projects.Any(x => x.Project.Slug == projectSlug && x.Notifications));
        }

        public DummyIssue CreateDummyIssue(Project project)
        {
            var startDate = DateTime.Parse("2016-06-01");
            var dumpBookings = session.Query<Booking>().Where(b => b.Project == project && b.Issue == null && b.Date >= startDate);
            DummyIssue dummy = new DummyIssue { Name = "DUMP: " + project.Name, Bookings = dumpBookings.ToList() };
            return dummy;
        }

        [MustHaveProject]
        public void View(string projectSlug, int issueId)
        {
            if (!string.IsNullOrEmpty(Request.UrlReferrer))
            {
                var referer = new Uri(Request.UrlReferrer);
                var routeMatch = RoutingModuleEx.Engine.FindMatch(referer.AbsolutePath, new RouteContext(Request, null, "/", new Hashtable()));
                if (routeMatch.Name == "issues") PropertyBag.Add("referer", referer.PathAndQuery);
            }

            var setting = SettingService.Load();
            PropertyBag.Add("setting", setting);

            var project = session.Slug<Project>(projectSlug);
            PropertyBag.Add("project", project);

            var item = session.Query<Issue>().Single(i => i.Number == issueId && i.Project == project);
            var statusString = string.Concat(Language.ResourceManager.GetString(item.State.ToString()).Select((currentChar, index) => index == 0 ? char.ToUpper(currentChar) : currentChar));
            var statusInt = item.State;

            var issueStates = EnumHelper.ToDictionary(typeof(IssueState));
            var statusList = issueStates.Where(s =>
                new[] { IssueState.Open, IssueState.Done, IssueState.Hold, IssueState.Closed, IssueState.Urgent }.Contains((IssueState)s.Key))
                .ToList();

            PropertyBag.Add("labels", CurrentUser.IsAdmin ? project.Labels : project.Labels.Where(l => l.VisibleForCustomer).ToList());
            PropertyBag.Add("item", item);
            PropertyBag.Add("users", session.Query<User>().Where(x => x.IsAdmin && x.IsActive).OrderBy(x => x.FullName));
            PropertyBag.Add("comments", item.Comments);
            PropertyBag.Add("days", DayHelper.GetPastDaysList(setting));
            PropertyBag.Add("statusList", statusList);
            PropertyBag.Add("status", statusInt);
            PropertyBag.Add("datetime", DateTime.Now);

            item.Touch(CurrentUser);
            foreach (var action in item.Actions)
            {
                action.Touch(CurrentUser);
            }
            using (var transaction = session.BeginTransaction())
            {
                session.SaveOrUpdate(item);
                transaction.Commit();
            }
        }

        [MustHaveProject]
        public void QuickView(string projectSlug, int issueId)
        {
            var project = session.Slug<Project>(projectSlug);
            PropertyBag.Add("project", project);
            var item = session.Query<Issue>().Single(i => i.Number == issueId && i.Project == project);
            PropertyBag.Add("item", item);
            PropertyBag.Add("comments", item.Comments);
            PropertyBag.Add("datetime", DateTime.Now);
            CancelLayout();
        }

        [MustHaveProject]
        public void BookingsChart(string projectSlug, int issueId)
        {
            var project = session.Slug<Project>(projectSlug);
            var item = session.Query<Issue>().Single(i => i.Number == issueId && i.Project == project);
            var totalTime = item.Bookings.Where(x => x.IsActive).Sum(x => x.Hours);
            var bookings = item.Bookings.Where(x => x.IsActive).GroupBy(x => x.User).ToDictionary(x => x.Key, x => new { percentage = Math.Round(x.Sum(y => y.Hours) / totalTime * 100), percentageBillable = Math.Round(x.Where(y => !y.Unbillable).Sum(y => y.Hours) / x.Sum(y => y.Hours) * 100), total = x.Sum(y => y.Hours), totalBillable = x.Where(y => !y.Unbillable).Sum(y => y.Hours) });
            PropertyBag.Add("project", project);
            PropertyBag.Add("item", item);
            PropertyBag.Add("bookings", bookings);
            PropertyBag.Add("totalBooked", item.BillableBookingHoursString());
            PropertyBag.Add("totalBookedUnbillable", item.UnbillableBookingHoursString());
            CancelLayout();
        }

        [MustHaveProject]
        public void New(string projectSlug)
        {
            var project = session.Slug<Project>(projectSlug);
            if (project.Closed)
            {
                throw new ProjectClosedException(project);
            }
            PropertyBag.Add("project", project);
            PropertyBag.Add("item", new Issue() { Project = project });
            PropertyBag.Add("labels", CurrentUser.IsAdmin ? project.Labels : project.Labels.Where(l => l.ApplicableByCustomer));
            RenderView("edit");
        }

        [Admin]
        public void Edit(string projectSlug, int issueId)
        {
            var project = session.Slug<Project>(projectSlug);
            if (project.Closed)
            {
                throw new ProjectClosedException(project);
            }
            PropertyBag.Add("project", project);
            var item = session.Query<Issue>().Single(i => i.Number == issueId && i.Project == project);
            PropertyBag.Add("item", item);
            PropertyBag.Add("labels", CurrentUser.IsAdmin ? project.Labels : project.Labels.Where(l => l.ApplicableByCustomer));
        }

        [MustHaveProject]
        public void Save(string projectSlug, int issueId, long[] labels, string andNew)
        {
            var project = session.Slug<Project>(projectSlug);
            if (project.Closed)
            {
                throw new ProjectClosedException(project);
            }

            var issue = session.Query<Issue>().SingleOrDefault(i => i.Number == issueId && i.Project == project);
            var query = session.Query<Label>().Where(x => x.IsActive && labels.Contains(x.Id)).ToList();
            var savedIssue = SaveIssue(project, issue, query);
            var labelUrgentCheckboxChecked = query.Any(l => l.LabelIsUrgent);

            if (issue == null && labelUrgentCheckboxChecked)
            {
                savedIssue.MakeUrgent(CurrentUser);
                savedIssue.Prioritized = true;
                savedIssue.Urgent = true;
                using (var tx = session.BeginTransaction())
                {
                    session.SaveOrUpdate(savedIssue);
                    tx.Commit();
                }
            }

            if (issue != null && labelUrgentCheckboxChecked)
            {
                if (issue.State != IssueState.Closed && issue.State != IssueState.Archived && issue.State != IssueState.Done && issue.State != IssueState.Hold && issue.State != IssueState.Urgent)
                {
                    savedIssue.MakeUrgent(CurrentUser);
                    savedIssue.Prioritized = true;
                    savedIssue.Urgent = true;
                    using (var tx = session.BeginTransaction())
                    {
                        session.SaveOrUpdate(savedIssue);
                        tx.Commit();
                    }
                }
            }

            if (issue != null && !labelUrgentCheckboxChecked)
            {
                if (issue.State != IssueState.Closed && issue.State != IssueState.Archived && issue.State != IssueState.Done && issue.State != IssueState.Hold && issue.State != IssueState.Urgent || issue.State == IssueState.Urgent)
                {
                    savedIssue.Open(CurrentUser);
                    savedIssue.Prioritized = false;
                    savedIssue.Urgent = false;
                    using (var tx = session.BeginTransaction())
                    {
                        session.SaveOrUpdate(savedIssue);
                        tx.Commit();
                    }
                }
            }

            using (var tx = session.BeginTransaction())
            {
                session.SaveOrUpdate(savedIssue);
                tx.Commit();
            }

            var hash = $"#issue{savedIssue.Number}";
            if (string.IsNullOrEmpty(andNew))
            {
                if (issue != null && (issue.State != IssueState.Open || issue.State != IssueState.Urgent))
                {
                    RedirectToUrl($"/project/{project.Slug}/issue/{issueId}/view");
                }
                else
                {
                    RedirectToUrl($"/project/{project.Slug}/issue/index{hash}");
                }
            }
            else
            {
                RedirectToUrl($"/project/{project.Slug}/issue/new");
            }
        }

        [return: JSONReturnBinder]
        public object AjaxSave(string projectSlug, int issueId, long[] labels)
        {
            var project = session.Slug<Project>(projectSlug);
            if (project.Closed)
            {
                throw new ProjectClosedException(project);
            }

            var issue = session.Query<Issue>().SingleOrDefault(i => i.Number == issueId && i.Project == project);
            var labelObjects = session.Query<Label>().Where(x => x.IsActive && labels.Contains(x.Id)).ToList();

            var savedIssue = SaveIssue(project, issue, labelObjects);
            var query = session.Query<Label>().Where(x => x.IsActive && labels.Contains(x.Id)).ToList();
            var labelUrgentCheckboxChecked = query.Any(l => l.LabelIsUrgent);

            if (issue == null && labelUrgentCheckboxChecked)
            {
                savedIssue.MakeUrgent(CurrentUser);
                savedIssue.Prioritized = true;
                savedIssue.Urgent = true;
                using (var tx = session.BeginTransaction())
                {
                    session.SaveOrUpdate(savedIssue);
                    tx.Commit();
                }
            }

            return new { savedIssue.Id, savedIssue.Number, savedIssue.Name };
        }

        private Issue SaveIssue(Project project, Issue issue, IEnumerable<Label> labels)
        {
            if (issue != null)
            {
                var name = Request.Params["item.Name"] == null ? issue.Name : Request.Params["item.Name"];
                BindObjectInstance(issue, "item");
                issue.Name = name.Replace("\"", "'");
                issue.Change(CurrentUser);
            }
            else
            {
                issue = BindObject<Issue>("item");
                issue.Number = project.NewIssueNumber;
                issue.Project = project;
                issue.Open(CurrentUser);
            }

            issue.Labels = labels.Select(label => session.Query<Label>().FirstOrDefault(l => l.Id == label.Id)).ToList();

            using (var transaction = session.BeginTransaction())
            {
                session.SaveOrUpdate(issue);
                transaction.Commit();
            }
            return issue;
        }


        [Admin]
        public void Pickup(string projectSlug, int issueId)
        {
            var project = session.Slug<Project>(projectSlug);
            if (project.Closed)
            {
                throw new ProjectClosedException(project);
            }
            var issue = session.Query<Issue>().Single(i => i.Number == issueId && i.Project == project);
            if (!issue.IsArchived && issue.PickedUpBy != CurrentUser)
            {
                issue.Pickup(CurrentUser);
                using (var transaction = session.BeginTransaction())
                {
                    session.SaveOrUpdate(issue);
                    transaction.Commit();
                }
            }
            RedirectToReferrer();
        }

        [Admin]
        public void HandOver(string projectSlug, int issueId, long userId)
        {
            var user = session.Get<User>(userId);
            var project = session.Slug<Project>(projectSlug);
            if (project.Closed)
            {
                throw new ProjectClosedException(project);
            }
            var issue = session.Query<Issue>().Single(i => i.Number == issueId && i.Project == project);
            if (!issue.IsArchived && user != CurrentUser && user != issue.PickedUpBy)
            {
                issue.HandOver(user, CurrentUser);
                using (var transaction = session.BeginTransaction())
                {
                    session.SaveOrUpdate(issue);
                    transaction.Commit();
                }
                emailService.SendHandOverNotification(issue.HandOvers.LastOrDefault());
            }
            RedirectToReferrer();
        }

        [MustHaveProject]
        public void AddComment(string projectSlug, int issueId, string body, bool isInternal)
        {
            RedirectToReferrer();
            var project = session.Slug<Project>(projectSlug);
            if (project.Closed)
            {
                throw new ProjectClosedException(project);
            }
            var issue = session.Query<Issue>().Single(i => i.Number == issueId && i.Project == project);
            if (issue.IsArchived) return;
            var comment = new Comment
            {
                Text = body,
                CreatedAt = DateTime.Now,
                Issue = issue,
                User = CurrentUser,
                IsInternal = isInternal
            };
            using (var transaction = session.BeginTransaction())
            {
                session.SaveOrUpdate(comment);
                transaction.Commit();
            }
        }

        public void ChangeCommentVisibility(int actionId)
        {
            var id = (long)actionId;
            var action = session.Get<Comment>(id);
            if (action == null || action.IsInternal != true)
                action.IsInternal = true;
            else
                action.IsInternal = false;

            using (var transaction = session.BeginTransaction())
            {
                session.SaveOrUpdate(action);
                transaction.Commit();
            }
            RedirectToReferrer();
        }

        [MustHaveProject]
        public void AddLabel(string projectSlug, int issueId, int param)
        {
            RedirectToReferrer();
            var project = session.Slug<Project>(projectSlug);
            if (project.Closed)
            {
                throw new ProjectClosedException(project);
            }
            var issue = session.Query<Issue>().Single(i => i.Number == issueId && i.Project == project);
            if (issue.IsArchived) return;
            var label = project.Labels.First(l => l.Id == param);
            issue.Labels.Add(label);
            if (label.LabelIsUrgent)
            {
                issue.MakeUrgent(CurrentUser);
                issue.Urgent = true;
                issue.Prioritized = true;
                using (var tx = session.BeginTransaction())
                {
                    session.SaveOrUpdate(issue);
                    tx.Commit();
                }
            }
            issue.Change(CurrentUser);

            using (var transaction = session.BeginTransaction())
            {
                session.SaveOrUpdate(issue);
                transaction.Commit();
            }
            RedirectToReferrer();
        }

        [MustHaveProject]
        public void DeleteLabel(string projectSlug, int issueId, int param)
        {
            RedirectToReferrer();
            var project = session.Slug<Project>(projectSlug);
            if (project.Closed)
            {
                throw new ProjectClosedException(project);
            }
            var issue = session.Query<Issue>().Single(i => i.Number == issueId && i.Project == project);
            if (issue.IsArchived) return;
            var label = project.Labels.First(l => l.Id == param);
            if (label.LabelIsUrgent)
            {
                issue.Open(CurrentUser);
                issue.Urgent = false;
                issue.Prioritized = false;
                using (var tx = session.BeginTransaction())
                {
                    session.SaveOrUpdate(issue);
                    tx.Commit();
                }
            }
            issue.Labels.Remove(label);
            issue.Change(CurrentUser);

            using (var transaction = session.BeginTransaction())
            {
                session.SaveOrUpdate(issue);
                transaction.Commit();
            }
            RedirectToReferrer();
        }

        [Admin]
        public void BookTime(string projectSlug, int issueId, DateTime date, double minutes, bool close, string comment, string status)
        {
            RedirectToReferrer();
            var project = session.Slug<Project>(projectSlug);
            if (project.Closed)
            {
                throw new ProjectClosedException(project);
            }
            var issue = session.Query<Issue>().Single(i => i.Number == issueId && i.Project == project);

            switch (status)
            {
                case "2":
                    issue.ChangeState(CurrentUser, IssueState.Done);
                    break;
                case "3":
                    issue.ChangeState(CurrentUser, IssueState.Hold);
                    break;
                case "4":
                    issue.ChangeState(CurrentUser, IssueState.Closed);
                    break;
                case "1":
                    issue.ChangeState(CurrentUser, IssueState.Open);
                    break;
                case "6":
                    issue.ChangeState(CurrentUser, IssueState.Urgent);
                    break;
            }

            if (issue.IsArchived) return;
            var booking = new Booking { User = CurrentUser, Date = date, Minutes = minutes, Issue = issue, Project = project, Comment = comment, Unbillable = project.Unbillable };

            using (var tx = session.BeginTransaction())
            {
                if (close)
                {
                    issue.Close(CurrentUser);
                    session.SaveOrUpdate(issue);
                }
                session.SaveOrUpdate(booking);
                tx.Commit();
            }
        }

        [MustHaveProject]
        public void Close(string projectSlug, int issueId)
        {
            RedirectToReferrer();
            var project = session.Slug<Project>(projectSlug);
            var issue = session.Query<Issue>().Single(i => i.Number == issueId && i.Project == project);
       
            if (issue.State != IssueState.Archived && issue.State != IssueState.Closed)
            {                
                issue.Prioritized = false;
                issue.Close(CurrentUser);
                using (var tx = session.BeginTransaction())
                {
                    session.SaveOrUpdate(issue);
                    tx.Commit();
                }
            }
        }

        [MustHaveProject]
        public void OnHold(string projectSlug, int issueId)
        {
            RedirectToReferrer();
            var project = session.Slug<Project>(projectSlug);
            var issue = session.Query<Issue>().Single(i => i.Number == issueId && i.Project == project);
        
            if (issue.State != IssueState.Archived && issue.State != IssueState.Hold)
            {
                issue.OnHold(CurrentUser);
                using (var tx = session.BeginTransaction())
                {
                    session.SaveOrUpdate(issue);
                    tx.Commit();
                }
            }
        }

        [MustHaveProject]
        public void Done(string projectSlug, int issueId)
        {
            RedirectToReferrer();
            var project = session.Slug<Project>(projectSlug);
            var issue = session.Query<Issue>().Single(i => i.Number == issueId && i.Project == project);
         
            if (issue.State != IssueState.Archived && issue.State != IssueState.Done)
            {
                issue.Done(CurrentUser);               
                issue.Prioritized = false;
                using (var tx = session.BeginTransaction())
                {
                    session.SaveOrUpdate(issue);
                    tx.Commit();
                }
            }
        }

        [MustHaveProject]
        public void Reopen(string projectSlug, int issueId)
        {
            RedirectToReferrer();
            var project = session.Slug<Project>(projectSlug);
            if (project.Closed)
            {
                throw new ProjectClosedException(project);
            }
            var issue = session.Query<Issue>().Single(i => i.Number == issueId && i.Project == project);
            if (issue.State != IssueState.Archived || issue.State != IssueState.Open)
            {
                if (issue.Labels.Any(l => l.LabelIsUrgent))
                {
                    issue.MakeUrgent(CurrentUser);
                }
                else
                {
                    issue.Open(CurrentUser);
                }

                using (var tx = session.BeginTransaction())
                {
                    session.SaveOrUpdate(issue);
                    tx.Commit();
                }
            }
        }

        [Admin]
        public void Archive(string projectSlug, int issueId)
        {
            RedirectToReferrer();
            if (!CurrentUser.IsAdmin) return;
            var project = session.Slug<Project>(projectSlug);
            var issue = session.Query<Issue>().Single(i => i.Number == issueId && i.Project == project);
      
            issue.Archive(CurrentUser);
            using (var tx = session.BeginTransaction())
            {                
                issue.Prioritized = false;
                session.SaveOrUpdate(issue);
                tx.Commit();
            }
        }

        [MustHaveProject]
        public void ReOrderIssue(string projectSlug, int issueNumber, int newIndex)
        {
            var issues = session.Query<Issue>().Where(x => x.Project.Slug == projectSlug && x.Pickups.Count == 0).ToList().Where(x => x.ChangeStates.Last().IssueState == IssueState.Open).OrderBy(x => x.PrioOrder).ToList();

            var issue = session.Query<Issue>().Single(x => x.Project.Slug == projectSlug && x.Number == issueNumber);
            issues.Remove(issue);
            issue.Prioritized = true;
            issues.Insert(newIndex, issue);

            var order = 1;
            using (var tx = session.BeginTransaction())
            {
                foreach (var currentissue in issues)
                {
                    currentissue.PrioOrder = order;
                    order++;
                    session.SaveOrUpdate(currentissue);
                }
                tx.Commit();
            }

            CancelView();
        }

        [return: JSONReturnBinder]
        public Dictionary<int, int> GetPrioOrder(string projectSlug)
        {
            var issues = session.Query<Issue>()
                .Where(x => x.Project.Slug == projectSlug && x.Pickups.Count == 0)
                .ToList()
                .Where(x => x.ChangeStates.Last().IssueState == IssueState.Open)
                .OrderByDescending(x => x.Prioritized)
                .Where(x => x.ChangeStates.Last().IssueState == IssueState.Urgent)
                .OrderByDescending(x => x.Prioritized)
                .ThenBy(x => x.PrioOrder)
                .ThenByDescending(x => x.Number);

            return issues.Select((x, i) => new { x.Number, i }).ToDictionary(x => x.Number, x => x.i);
        }

        [MustHaveProject]
        public void ExportImport(string projectSlug)
        {
            var project = session.Slug<Project>(projectSlug);

            PropertyBag.Add("project", project);
        }

        [MustHaveProject]
        public void ExportJson(string projectSlug, string[] selectedLabels, IssueState state, string issues)
        {
            var project = session.Slug<Project>(projectSlug);

            if (Request.Params["state"] == null) state = CurrentUser.DefaultState;
            var items = project.Issues;

            if (string.IsNullOrEmpty(issues))
            {
                if (selectedLabels.Length > 0)
                    items = items.Where(
                            i => i.Labels.Select(l => l.Name).Intersect(selectedLabels).Count() == selectedLabels.Length).ToList();
            }
            else
            {
                items = items.Where(i => issues.Split(',').Contains(i.Number.ToString())).ToList();
            }

            var json = JsonConvert.SerializeObject(items, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore, DateParseHandling = DateParseHandling.None });

            CancelView();

            Response.ClearContent();
            Response.Clear();

            var filename = $"issues_{project.Slug}_{DateTime.Now:yyyyMMdd_hhmm}.json";

            Response.AppendHeader("content-disposition", $"attachment; filename={filename}");
            Response.ContentType = "application/json";

            var byteArray = Encoding.Default.GetBytes(json);
            var stream = new MemoryStream(byteArray);
            Response.BinaryWrite(stream);
        }

        [MustHaveProject]
        public void ExportCsv(string projectSlug, string[] selectedLabels, IssueState state, string issues)
        {
            var project = session.Slug<Project>(projectSlug);

            if (Request.Params["state"] == null) state = CurrentUser.DefaultState;
            var items = project.Issues;

            if (string.IsNullOrEmpty(issues))
            {
                if (selectedLabels.Length > 0)
                    items = items.Where(
                            i => i.Labels.Select(l => l.Name).Intersect(selectedLabels).Count() == selectedLabels.Length).ToList();
            }
            else
            {
                items = items.Where(i => issues.Split(',').Contains(i.Number.ToString())).ToList();
            }

            var csv = CsvHelper.IssuesCsv(project, items);
            CancelView();

            Response.ClearContent();
            Response.Clear();

            var filename = $"issues_{project.Slug}_{DateTime.Now:yyyyMMdd_hhmm}.csv";

            Response.AppendHeader("content-disposition", $"attachment; filename={filename}");
            Response.ContentType = "application/csv";

            var byteArray = Encoding.Default.GetBytes(csv);
            var stream = new MemoryStream(byteArray);
            Response.BinaryWrite(stream);
        }

        [return: JSONReturnBinder]
        public object Autocomplete(long projectId, string query)
        {
            var suggestions = new List<Suggestion>();
            var issues = session.Query<Issue>().Where(i => i.Project.Id == projectId && i.IsActive);
            if (query != null)
            {
                issues = issues.Where(i => i.Number.ToString().Contains(query) || i.Name.Contains(query));
            }
            suggestions.AddRange(issues.ToList().Where(i => !i.IsArchived && i.HasBeenOpenSince(DateTime.Today.AddDays(-7))).Select(x => new Suggestion($"#{x.Number} - {x.Name}", x.Id.ToString(), x.State.ToString())));
            return new { query = query, suggestions = suggestions };
        }

        [return: JSONReturnBinder]
        public object Suggestions(long projectId, string query)
        {
            var suggestions = new List<Suggestion>();

            query = query?.ToLower() ?? "";

            var issues = session.Query<Issue>().Where(i => i.Project.Id == projectId && i.IsActive).ToList().Where(i => !i.IsArchived).ToList();

            if (!string.IsNullOrEmpty(query))
            {
                issues = issues.Where(x => x.Number.ToString().StartsWith(query) || x.Name.ToLower().Contains(query) || x.Comments.Any(c => c.Id.ToString().StartsWith(query))).Take(10).ToList();
            }

            foreach (var issue in issues)
            {
                suggestions.Add(new Suggestion($"#{issue.Number} - {issue.Name}", issue.Number.ToString(), "issuelink"));
                var comments = issue.Comments;
                suggestions.AddRange(comments.Select(c => new Suggestion($"#{issue.Number}#{c.Id} - {c.Name} ({c.CreatedAt})", $"{issue.Number}#{c.Id}", "commentlink")));
            }
            return suggestions;
        }

        [Admin]
        public void Subscribe(string projectSlug)
        {
            RedirectToReferrer();

            var project = session.SlugOrDefault<Project>(projectSlug);

            var projectRelation = CurrentUser.Projects.FirstOrDefault(x => x.Project.Slug == projectSlug) ?? new UserProject { User = CurrentUser, Project = project };

            projectRelation.Notifications = true;

            using (var tx = session.BeginTransaction())
            {
                session.SaveOrUpdate(projectRelation);
                tx.Commit();
            }
        }

        [Admin]
        public void Unsubscribe(string projectSlug)
        {
            RedirectToReferrer();

            var projectRelation = CurrentUser.Projects.FirstOrDefault(x => x.Project.Slug == projectSlug);

            if (projectRelation == null) return;

            projectRelation.Notifications = false;

            using (var tx = session.BeginTransaction())
            {
                session.SaveOrUpdate(projectRelation);
                tx.Commit();
            }
        }

        [Admin]
        public void MuteProject(string projectSlug)
        {
            RedirectToReferrer();

            var project = session.SlugOrDefault<Project>(projectSlug);

            project.IsMuted = true;

            using (var tx = session.BeginTransaction())
            {
                session.SaveOrUpdate(project);
                tx.Commit();
            }
        }

        [Admin]
        public void UnmuteProject(string projectSlug)
        {
            RedirectToReferrer();

            var project = session.SlugOrDefault<Project>(projectSlug);

            project.IsMuted = false;

            using (var tx = session.BeginTransaction())
            {
                session.SaveOrUpdate(project);
                tx.Commit();
            }
        }

        [Danielle]
        public void ChangeProject(string sourceProjectSlug, string issueNumbers, string targetProjectSlug, bool renumber)
        {
            if (targetProjectSlug == null)
            {
                Error("selecteer een project", true);
                return;
            }

            if (issueNumbers == null)
            {
                Error("selecteer een taak", true);
                return;
            }

            var sourceProject = session.SlugOrDefault<Project>(sourceProjectSlug);
            var targetProject = session.SlugOrDefault<Project>(targetProjectSlug);

            var issues = session.Query<Issue>().Where(x => issueNumbers.Split(',').Select(int.Parse).Contains(x.Number) && x.Project == sourceProject).OrderBy(x => x.Number).ToList();

            var invoicedIssues = issues.Where(x => x.Invoices.Any(i => i.IsActive && !i.IsArchived)).ToList();

            if (invoicedIssues.Any())
            {
                Error($"Gefactureerde taken mogen niet verplaatst worden.\r\nTaken: {string.Join(", ", invoicedIssues.Select(x => x.Number))}", true);
                return;
            }

            using (var transaction = session.BeginTransaction())
            {
                var maxIssueNumber = targetProject.Issues.Except(sourceProject.Issues.Intersect(issues)).Any() ? targetProject.Issues.Except(sourceProject.Issues.Intersect(issues)).Max(x => x.Number) : 0;

                foreach (var issue in issues)
                {
                    //var issue = session.Query<Issue>().Single(x => x.Number == issueNumber && x.Project == sourceProject);

                    //if (issue.Invoices.Any())
                    //{
                    //    Error($"ticket {issue.Number} is gefactureerd", true);
                    //    transaction.Rollback();
                    //    return;
                    //}

                    foreach (var sourceLabel in issue.Labels.ToList())
                    {
                        var targetLabel =
                            targetProject.Labels.FirstOrDefault(
                                x => x.IsActive
                                     && x.ApplicableByCustomer == sourceLabel.ApplicableByCustomer
                                     && x.Color == sourceLabel.Color
                                     && (x.Name == sourceLabel.Name || x.Name == $"{sourceLabel.Name} (copy from {sourceProject.Name})")
                                     //&& x.ToFreckle == sourceLabel.ToFreckle
                                     && x.VisibleForCustomer == sourceLabel.VisibleForCustomer);

                        if (targetLabel == null)
                        {
                            targetLabel = new Label
                            {
                                Project = targetProject,
                                ApplicableByCustomer = sourceLabel.ApplicableByCustomer,
                                Color = sourceLabel.Color,
                                //Name = sourceLabel.Name,
                                Name = $"{sourceLabel.Name} (copy from {sourceProject.Name})",
                                ToFreckle = sourceLabel.ToFreckle,
                                VisibleForCustomer = sourceLabel.VisibleForCustomer
                            };

                            targetProject.Labels.Add(targetLabel);

                            session.SaveOrUpdate(targetLabel);
                        }

                        issue.Labels.Remove(sourceLabel);
                        issue.Labels.Add(targetLabel);
                    }

                    foreach (var booking in issue.Bookings.ToList())
                    {
                        sourceProject.Bookings.Remove(booking);
                        booking.Project = targetProject;
                        targetProject.Bookings.Add(booking);

                        session.SaveOrUpdate(booking);
                    }

                    issue.Comments.Add(
                        new Comment
                        {
                            Text = $"Dit ticket is verplaatst van {sourceProject.Name} en had daar nummer {issue.Number}.",
                            CreatedAt = DateTime.Now,
                            Issue = issue,
                            User = CurrentUser,
                            IsInternal = true
                        });

                    sourceProject.Issues.Remove(issue);
                    issue.Project = targetProject;
                    if (renumber || issue.Number <= maxIssueNumber) issue.Number = ++maxIssueNumber;
                    targetProject.Issues.Add(issue);

                    session.SaveOrUpdate(issue);
                }

                transaction.Commit();
            }

            if (session.Query<PlanningItem>().Any(x => x.Resource == $"p{sourceProject}" && x.Start >= DateTime.Today))
            {
                Information("Er is een toekomstige planning op het oude project. Deze wordt niet automatisch aangepast.");
            }

            RedirectToUrl($"/project/{targetProject.Slug}/issues");
        }
    }
}