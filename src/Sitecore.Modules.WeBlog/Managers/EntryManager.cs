﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Modules.WeBlog.Data.Items;
using Sitecore.Modules.WeBlog.Extensions;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Linq.Utilities;
using Sitecore.ContentSearch.Security;
using Sitecore.Modules.WeBlog.Configuration;
using Sitecore.Modules.WeBlog.Diagnostics;
using Sitecore.Modules.WeBlog.Model;
using Sitecore.Modules.WeBlog.Search;
using Sitecore.Modules.WeBlog.Search.SearchTypes;
using Sitecore.Modules.WeBlog.Caching;
#if FEATURE_XCONNECT
using Sitecore.Modules.WeBlog.Analytics.Reporting;
using Sitecore.Xdb.Reporting;
#elif FEATURE_XDB
using Sitecore.Modules.WeBlog.Analytics.Reporting;
using Sitecore.Analytics.Reporting;
#elif FEATURE_DMS
using Sitecore.Analytics.Data.DataAccess.DataAdapters;
#else
using Sitecore.Analytics.Reports.Data.DataAccess.DataAdapters;
#endif

namespace Sitecore.Modules.WeBlog.Managers
{
    /// <summary>
    /// Provides utilities for working with blog entries.
    /// </summary>
    public class EntryManager : IEntryManager
    {
        /// <summary>
        /// The settings to use.
        /// </summary>
        protected IWeBlogSettings Settings = null;

        /// <summary>
        /// The cache used to store blog entries in.
        /// </summary>
        protected IEntrySearchCache EntryCache = null;

        /// <summary>
        /// The comment manager to use.
        /// </summary>
        protected ICommentManager CommentManager = null;

#if FEATURE_XDB
        /// <summary>The <see cref="ReportDataProviderBase"/> to read reporting data from.</summary>
        protected ReportDataProviderBase ReportDataProvider = null;

        public EntryManager()
            : this(null, new EntrySearchCache())
        {
        }

        public EntryManager(
            ReportDataProviderBase reportDataProvider,
            IEntrySearchCache cache,
            IWeBlogSettings settings = null,
            ICommentManager commentManager = null)
        {
            ReportDataProvider = reportDataProvider;
            Settings = settings ?? WeBlogSettings.Instance;
            EntryCache = cache;
            CommentManager = commentManager ?? ManagerFactory.CommentManagerInstance;
        }
#else
        public EntryManager()
            : this(null)
        {
        }

        public EntryManager(IWeBlogSettings settings = null, BaseCacheManager cacheManager = null)
        {
            Settings = settings ?? WeBlogSettings.Instance;
            SetEntriesCache(cacheManager);
        }
#endif

        /// <summary>
        /// Deletes a blog post.
        /// </summary>
        /// <param name="postId">The ID of the post to delete.</param>
        /// <param name="db">The database to delete the entry from.</param>
        /// <returns>True if the post was deleted, otherwise False.</returns>
        public virtual bool DeleteEntry(string postId, Database db)
        {
            Assert.IsNotNull(db, "Database cannot be null");

            if (!string.IsNullOrEmpty(postId))
            {
                var blogPost = db.GetItem(postId);

                if (blogPost != null)
                {
                    try
                    {
                        blogPost.Delete();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Failed to delete blog post " + postId, ex, this);
                        return false;
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets blog entries for the given blog which meet the criteria.
        /// </summary>
        /// <param name="blogRootItem">The root item of the blog to retrieve the entries for.</param>
        /// <param name="criteria">The criteria the entries should meet.</param>
        /// <returns>The entries matching the criteria.</returns>
        public virtual IList<Entry> GetBlogEntries(Item blogRootItem, EntryCriteria criteria)
        {
            if (blogRootItem == null || criteria == null || criteria.PageNumber <= 0 || criteria.PageSize <= 0)
                return new Entry[0];

            var cachedEntries = EntryCache?.Get(criteria);

            if (cachedEntries != null)
                return cachedEntries;

            var customBlogItem = (from templateId in Settings.BlogTemplateIds
                                  where blogRootItem.TemplateIsOrBasedOn(templateId)
                                  select (BlogHomeItem)blogRootItem).FirstOrDefault();

            if (customBlogItem == null)
            {
                customBlogItem = (from templateId in Settings.BlogTemplateIds
                                  let item = blogRootItem.FindAncestorByTemplate(templateId)
                                  where item != null
                                  select (BlogHomeItem)item).FirstOrDefault();
            }

            if (customBlogItem == null)
                return new Entry[0];

            var indexName = Settings.SearchIndexName;

            if (!string.IsNullOrEmpty(indexName))
            {

                using (var context = ContentSearchManager.GetIndex(indexName + "-" + blogRootItem.Database.Name).CreateSearchContext(SearchSecurityOptions.DisableSecurityCheck))
                {
                    var builder = PredicateBuilder.True<EntryResultItem>();

                    builder = builder.And(i => i.TemplateId == customBlogItem.BlogSettings.EntryTemplateID);
                    builder = builder.And(i => i.Paths.Contains(customBlogItem.ID));
                    builder = builder.And(i => i.Language.Equals(customBlogItem.InnerItem.Language.Name, StringComparison.InvariantCulture));
                    builder = builder.And(item => item.DatabaseName.Equals(Context.Database.Name, StringComparison.InvariantCulture));

                    // Tag
                    if (!string.IsNullOrEmpty(criteria.Tag))
                    {
                        builder = builder.And(i => i.Tags.Contains(criteria.Tag));
                    }

                    // Categories
                    if (!string.IsNullOrEmpty(criteria.Category))
                    {
                        var categoryItem = ManagerFactory.CategoryManagerInstance.GetCategory(customBlogItem, criteria.Category);

                        // If the category is unknown, don't return any results.
                        if (categoryItem == null)
                            return new Entry[0];

                        builder = builder.And(i => i.Category.Contains(categoryItem.ID));
                    }

                    if (criteria.MinimumDate != null)
                        builder = builder.And(i => i.EntryDate >= criteria.MinimumDate);

                    if (criteria.MaximumDate != null)
                        builder = builder.And(i => i.EntryDate < criteria.MaximumDate);

                    var indexresults = context.GetQueryable<EntryResultItem>().Where(builder);

                    if (indexresults.Any())
                    {
                        var itemResults = indexresults.OrderByDescending(item => item.EntryDate)
                            .ThenByDescending(item => item.CreatedDate)
                            .Skip(criteria.PageSize * (criteria.PageNumber - 1))
                            .Take(criteria.PageSize);

                        var entries = itemResults.Select(x => CreateEntry(x)).ToList();
                        EntryCache?.Set(criteria, entries);

                        return entries;
                    }
                }
            }

            return new Entry[0];
        }

        protected virtual Entry CreateEntry(EntryResultItem resultItem)
        {
            // todo: create tag parser

            return new Entry
            {
                Uri = resultItem.Uri,
                Title = string.IsNullOrWhiteSpace(resultItem.Title) ? resultItem.Name : resultItem.Title,
                Tags = resultItem.Tags != null ? resultItem.Tags.Split(',').Select(x => x.Trim()) : Enumerable.Empty<string>(),
                EntryDate = resultItem.EntryDate
            };
        }

        /// <summary>
        /// Gets the most popular entries for the blog by the number of page views.
        /// </summary>
        /// <param name="blogItem">The blog to find the most popular pages for.</param>
        /// <param name="maxCount">The maximum number of entries to return.</param>
        /// <returns>The <see cref="ItemUri"/> for the entry items.</returns>
        public virtual IList<ItemUri> GetPopularEntriesByView(Item blogItem, int maxCount)
        {
            var analyticsEnabled = IsAnalyticsEnabled();
            if (analyticsEnabled)
            {
                var blogEntries = GetBlogEntries(blogItem, EntryCriteria.AllEntries);

                return blogEntries.OrderByDescending(x => GetItemViews(x.Uri.ItemID)).Select(x => x.Uri).Take(maxCount).ToList();
            }
            
            Logger.Warn("Sitecore.Analytics must be enabled to get popular entries by view.", this);

            return new ItemUri[0];
        }

        /// <summary>
        /// Gets the most popular entries for the blog by the number of comments on the entry.
        /// </summary>
        /// <param name="blogItem">The blog to find the most popular pages for.</param>
        /// <param name="maxCount">The maximum number of entries to return.</param>
        /// <returns>The <see cref="ItemUri"/> for the entry items.</returns>
        public virtual IList<ItemUri> GetPopularEntriesByComment(Item blogItem, int maxCount)
        {
            var uris = CommentManager.GetMostCommentedEntries(blogItem, maxCount);
            return uris;
        }

        /// <summary>
        /// Gets the entry item for the given comment.
        /// </summary>
        /// <param name="commentUri">The <see cref="ItemUri"/> of the comment item.</param>
        /// <returns>The <see cref="EntryItem"/> that owns the comment.</returns>
        public virtual EntryItem GetBlogEntryItemByCommentUri(ItemUri commentUri)
        {
            if (commentUri == null)
                return null;

            var commentItem = Database.GetItem(commentUri);

            return commentItem?.FindAncestorByAnyTemplate(Settings.EntryTemplateIds);
        }

        protected static bool IsAnalyticsEnabled()
        {
            return Sitecore.Configuration.Settings.GetBoolSetting("Analytics.Enabled", false) ||
                   Sitecore.Configuration.Settings.GetBoolSetting("Xdb.Enabled", false);
        }

        /// <summary>
        /// Gets the number of views for the item given by ID.
        /// </summary>
        /// <param name="itemId">The ID of the item to get the views for.</param>
        /// <returns>The number of views for the item.</returns>
        protected virtual long GetItemViews(ID itemId)
        {
#if FEATURE_XDB
                        var query = new ItemVisitsQuery(this.ReportDataProvider)
                        {
                            ItemId = itemId
                        };

                        query.Execute();

                        return query.Visits;
#elif FEATURE_DMS
            var queryId = itemId.ToString().Replace("{", string.Empty).Replace("}", string.Empty);
            var sql = "SELECT COUNT(ItemId) as Visits FROM {{0}}Pages{{1}} WHERE {{0}}ItemId{{1}} = '{0}'".FormatWith(queryId);

            return DataAdapterManager.ReportingSql.ReadOne(sql, reader => DataAdapterManager.ReportingSql.GetLong(0, reader));
#endif
        }
    }
}