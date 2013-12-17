﻿using System;
using System.IO;
using System.Text;
using Castle.MonoRail.Framework;
using Gitle.Model;
using Gitle.Web.Filters;

namespace Gitle.Web.Controllers.Admin
{
    using System.Collections.Generic;
    using System.Reflection;
    using System.Security;
    using Helpers;

    [Layout("secure")]
    [Filter(ExecuteWhen.BeforeAction, typeof (AuthorisationFilter), ExecutionOrder = 1)]
    [Filter(ExecuteWhen.BeforeAction, typeof (AuthenticationFilter), ExecutionOrder = 2)]
    public abstract class SecureController : BaseController
    {
        public User CurrentUser
        {
            get { return (User) Context.CurrentUser; }
        }

        protected override object InvokeMethod(MethodInfo method, IRequest request,
                                               IDictionary<string, object> extraArgs)
        {
            var adminAttributes = (AdminAttribute[])method.GetCustomAttributes(typeof(AdminAttribute), false);
            if (adminAttributes.Length > 0 && !CurrentUser.IsAdmin)
            {
                RenderView("/shared/forbidden");
                return null;
            }

            base.InvokeMethod(method, request, extraArgs);
            return null;
        }
        
        protected void ServeCsv(string exportName, string csv)
        {
            CancelView();

            Response.ClearContent();
            Response.Clear();

            Response.AppendHeader("content-disposition",
                                  string.Format("attachment; filename={0}",
                                                string.Format("export_{0:yyyyMMdd_hhmmss}.csv",
                                                              DateTime.Now)));
            Response.ContentType = "application/csv";

            byte[] byteArray = Encoding.Default.GetBytes(csv);
            var stream = new MemoryStream(byteArray);
            Response.BinaryWrite(stream);
        }
    }
}