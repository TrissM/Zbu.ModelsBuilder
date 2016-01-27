﻿using System;
using System.Collections.Generic;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Umbraco.Core;
using Umbraco.Core.Configuration;
using Umbraco.Core.IO;
using Umbraco.Core.Models.PublishedContent;
using Umbraco.Core.Services;
using Umbraco.Core.Strings;
using Umbraco.ModelsBuilder.Configuration;
using Umbraco.ModelsBuilder.Umbraco;
using Umbraco.Web;
using Umbraco.Web.UI.JavaScript;

namespace Umbraco.ModelsBuilder.AspNet
{
    public class ModelsBuilderApplication : ApplicationEventHandler
    {
        protected override void ApplicationStarted(UmbracoApplicationBase umbracoApplication, ApplicationContext applicationContext)
        {
            FileService.SavingTemplate += FileService_SavingTemplate;
        }

        /// <summary>
        /// Used to check if a template is being created based on a document type, in this case we need to
        /// ensure the template markup is correct based on the model name of the document type
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileService_SavingTemplate(IFileService sender, Core.Events.SaveEventArgs<Core.Models.ITemplate> e)
        {
            // don't do anything if we're not enabled
            if (!UmbracoConfig.For.ModelsBuilder().Enable) return;

            // don't do anything if this special key is not found
            if (!e.AdditionalData.ContainsKey("CreateTemplateForContentType")) return;

            // ensure we have the content type alias
            if (!e.AdditionalData.ContainsKey("ContentTypeAlias"))
                throw new InvalidOperationException("The additionalData key: ContentTypeAlias was not found");

            foreach (var template in e.SavedEntities)
            {
                // if it is in fact a new entity (not been saved yet) and the "CreateTemplateForContentType" key
                // is found, then it means a new template is being created based on the creation of a document type
                if (!template.HasIdentity && template.Content.IsNullOrWhiteSpace())
                {
                    // ensure is safe and always pascal cased, per razor standard
                    // + this is how we get the default model name in Umbraco.ModelsBuilder.Umbraco.Application
                    var className = e.AdditionalData["ContentTypeAlias"].ToString().ToCleanString(CleanStringType.ConvertCase | CleanStringType.PascalCase);

                    var modelNamespace = UmbracoConfig.For.ModelsBuilder().ModelsNamespace;

                    // we do not support configuring this at the moment, so just let Umbraco use its default value
                    //var modelNamespaceAlias = ...;

                    var markup = ViewHelper.GetDefaultFileContent(
                        modelClassName: className,
                        modelNamespace: modelNamespace/*,
                        modelNamespaceAlias: modelNamespaceAlias*/);

                    //set the template content to the new markup
                    template.Content = markup;
                }
            }
        }

        protected override void ApplicationStarting(UmbracoApplicationBase umbracoApplication, ApplicationContext applicationContext)
        {
            // install pure-live models if required

            if (UmbracoConfig.For.ModelsBuilder().ModelsMode == ModelsMode.PureLive)
                ApplicationStartingLiveModels(applicationContext);

            // always setup the dashboard

            RegisterServerVars();
        }

        private void ApplicationStartingLiveModels(ApplicationContext applicationContext)
        {
            var logger = applicationContext.ProfilingLogger;
            var factory = new PureLiveModelFactory(logger);
            PublishedContentModelFactoryResolver.Current.SetFactory(factory);

            // the following would add @using statement in every view so user's don't
            // have to do it - however, then noone understands where the @using statement
            // comes from, and it cannot be avoided / removed --- DISABLED
            //
            /*
            // no need for @using in views
            // note:
            //  we are NOT using the in-code attribute here, config is required
            //  because that would require parsing the code... and what if it changes?
            //  we can AddGlobalImport not sure we can remove one anyways
            var modelsNamespace = Configuration.Config.ModelsNamespace;
            if (string.IsNullOrWhiteSpace(modelsNamespace))
                modelsNamespace = Configuration.Config.DefaultModelsNamespace;
            System.Web.WebPages.Razor.WebPageRazorHost.AddGlobalImport(modelsNamespace);
            */
        }

        /// <summary>
        /// Add custom server variables for angular to use
        /// </summary>
        private void RegisterServerVars()
        {
            // register our url - for the backoffice api
            ServerVariablesParser.Parsing += (sender, serverVars) =>
            {
                if (!serverVars.ContainsKey("umbracoUrls"))
                    throw new Exception("Missing umbracoUrls.");
                var umbracoUrlsObject = serverVars["umbracoUrls"];
                if (umbracoUrlsObject == null)
                    throw new Exception("Null umbracoUrls");
                var umbracoUrls = umbracoUrlsObject as Dictionary<string, object>;
                if (umbracoUrls == null)
                    throw new Exception("Invalid umbracoUrls");

                if (!serverVars.ContainsKey("umbracoPlugins"))
                    throw new Exception("Missing umbracoPlugins.");
                var umbracoPlugins = serverVars["umbracoPlugins"] as Dictionary<string, object>;
                if (umbracoPlugins == null)
                    throw new Exception("Invalid umbracoPlugins");

                if (HttpContext.Current == null) throw new InvalidOperationException("HttpContext is null");
                var urlHelper = new UrlHelper(new RequestContext(new HttpContextWrapper(HttpContext.Current), new RouteData()));

                umbracoUrls["modelsBuilderBaseUrl"] = urlHelper.GetUmbracoApiServiceBaseUrl<ModelsBuilderBackOfficeController>(controller => controller.BuildModels());
                umbracoPlugins["modelsBuilder"] = GetModelsBuilderSettings();
            };
        }

        private Dictionary<string, object> GetModelsBuilderSettings()
        {
            if (ApplicationContext.Current.IsConfigured == false)
                return null;

            var settings = new Dictionary<string, object>
                {
                    {"enabled", UmbracoConfig.For.ModelsBuilder().Enable}
                };

            return settings;
        }
    }
}