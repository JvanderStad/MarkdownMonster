﻿
#region License
/*
 **************************************************************
 *  Author: Rick Strahl 
 *          © West Wind Technologies, 2016
 *          http://www.west-wind.com/
 * 
 * Created: 05/15/2016
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 **************************************************************  
*/
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Xml;
using FontAwesome.WPF;
using HtmlAgilityPack;
using WebLogAddin.MetaWebLogApi;
using MarkdownMonster;
using MarkdownMonster.AddIns;
using WebLogAddin.Medium;
using Westwind.Utilities;
using File = System.IO.File;

namespace WeblogAddin
{
    public class WebLogAddin :  MarkdownMonsterAddin, IMarkdownMonsterAddin
    {
        private Post ActivePost { get; set; } = new Post();


        public override void OnApplicationStart()
        {
            base.OnApplicationStart();

            Id = "weblog";

            // Create addin and automatically hook menu events
            var menuItem = new AddInMenuItem(this)
            {
                Caption = "Weblog Publishing",                
                FontawesomeIcon = FontAwesomeIcon.Rss,
                KeyboardShortcut = WeblogAddinConfiguration.Current.KeyboardShortcut
            };

            MenuItems.Add(menuItem);
        }

        public override void OnExecute(object sender)
        {
            // read settings on startup
            WeblogAddinConfiguration.Current.Read();

            var form = new WebLogForm()
            {
                Owner = Model.Window
            };
            form.Model.AppModel = Model;
            form.Model.Addin = this;                       
            form.Show();                       
        }

        public override bool OnCanExecute(object sender)
        {
            return Model.IsEditorActive;
        }

        public override void OnExecuteConfiguration(object sender)
        {
            string file = Path.Combine(mmApp.Configuration.CommonFolder, "weblogaddin.json");
            Model.Window.OpenTab(file);
        }


        public override void OnNotifyAddin(string command, object parameter)
        {
            if (command == "newweblogpost")
            {
                var form = new WebLogForm()
                {
                    Owner = Model.Window
                };
                form.Model.AppModel = Model;
                form.Model.Addin = this;
                form.Show();
                form.TabControl.SelectedIndex = 1;
            }            
        }

        #region Post Send Operations

        /// <summary>
        /// High level method that sends posts to the Weblog
        /// 
        /// </summary>
        /// <returns></returns>
        public bool SendPost(WeblogInfo weblogInfo, bool sendAsDraft = false)
        {

            var editor = Model.ActiveEditor;
            if (editor == null)
                return false;

            var doc = editor.MarkdownDocument;

            ActivePost = new Post()
            {
                DateCreated = DateTime.Now
            };

            // start by retrieving the current Markdown from the editor
            string markdown = editor.GetMarkdown();



            // Retrieve Meta data from post and clean up the raw markdown
            // so we render without the config data
            var meta = GetPostConfigFromMarkdown(markdown, weblogInfo);

            string html = doc.RenderHtml(meta.MarkdownBody, WeblogAddinConfiguration.Current.RenderLinksOpenExternal);
            ActivePost.Body = html;
            ActivePost.PostID = meta.PostId;


            var customFields = new List<CustomField>();
            if (!string.IsNullOrEmpty(markdown))
            {
                customFields.Add( new CustomField() { ID = "mt_markdown", Key = "mt_markdown", Value = markdown });
            }
            if (weblogInfo.CustomFields != null)
            {
                foreach (var kvp in weblogInfo.CustomFields)                
                    customFields.Add( new CustomField { ID = kvp.Key,Key = kvp.Key, Value = kvp.Value});                
            }
            if (meta.CustomFields != null)
            {
                foreach (var kvp in meta.CustomFields)
                    customFields.Add(new CustomField { Key = kvp.Key, ID = kvp.Key, Value = kvp.Value });
                
            }
            ActivePost.CustomFields = customFields.ToArray();

            var config = WeblogAddinConfiguration.Current;

            var kv = config.Weblogs.FirstOrDefault(kvl => kvl.Value.Name == meta.WeblogName);
            if (kv.Equals(default(KeyValuePair<string, WeblogInfo>)))
            {
                MessageBox.Show("Invalid Weblog configuration selected.",
                    "Weblog Posting Failed",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return false;
            }
            weblogInfo = kv.Value;

            var type = weblogInfo.Type;
            if (type == WeblogTypes.Unknown)
                type = weblogInfo.Type;


            string basePath = Path.GetDirectoryName(doc.Filename);
            string postUrl = null;

            if (type == WeblogTypes.MetaWeblogApi || type == WeblogTypes.Wordpress)
            {
                MetaWebLogWordpressApiClient client;
                client = new MetaWebLogWordpressApiClient(weblogInfo);

                // if values are already configured don't overwrite them again
                client.FeaturedImageUrl = meta.FeaturedImageUrl;
                client.FeatureImageId = meta.FeatureImageId;

                if (!client.PublishCompletePost(ActivePost, basePath,
                    sendAsDraft, markdown))
                {
                    mmApp.Log($"Error sending post to Weblog at {weblogInfo.ApiUrl}: " + client.ErrorMessage);
                    MessageBox.Show($"Error sending post to Weblog: " + client.ErrorMessage,
                        mmApp.ApplicationName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Exclamation);
                    return false;
                }
                postUrl = client.GetPostUrl(ActivePost.PostID);
            }
            if (type == WeblogTypes.Medium)
            {
                var client = new MediumApiClient(weblogInfo);
                var result = client.PublishCompletePost(ActivePost, basePath, sendAsDraft);
                if (result == null)
                {
                    mmApp.Log($"Error sending post to Weblog at {weblogInfo.ApiUrl}: " + client.ErrorMessage);
                    MessageBox.Show($"Error sending post to Weblog: " + client.ErrorMessage,
                        mmApp.ApplicationName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Exclamation);
                    return false;
                }
                // this is null
                postUrl = client.PostUrl;
            }

            meta.PostId = ActivePost.PostID.ToString();

            // retrieve the raw editor markdown
            markdown = editor.GetMarkdown();
            meta.RawMarkdownBody = markdown;

            // add the meta configuration to it
            markdown = SetConfigInMarkdown(meta);

            // write it back out to editor
            editor.SetMarkdown(markdown);

            // preview post
            if (!string.IsNullOrEmpty(weblogInfo.PreviewUrl))
            {
                var url = weblogInfo.PreviewUrl.Replace("{0}", ActivePost.PostID.ToString());
                ShellUtils.GoUrl(url);
            }
            else
            {
                if (postUrl != null)
                    ShellUtils.GoUrl(postUrl);
                else
                    ShellUtils.GoUrl(new Uri(weblogInfo.ApiUrl).GetLeftPart(UriPartial.Authority));
            }

            return true;

        }



        #endregion

        #region Local Post Creation

        public string NewWeblogPost(WeblogPostMetadata meta)
        {
            if (meta == null)
            {
                meta = new WeblogPostMetadata()
                {
                    Title = "Post Title",
                };
            }


            if (string.IsNullOrEmpty(meta.WeblogName))
                meta.WeblogName = "Name of registered blog to post to";
            

            string post =
                $@"# {meta.Title}

{meta.MarkdownBody}

<!-- Post Configuration -->
<!--
```xml
<blogpost>
<title>{meta.Title}</title>
<abstract>
{meta.Abstract}
</abstract>
<categories>
{meta.Categories}
</categories>
<isDraft>{meta.IsDraft}</isDraft>
<featuredImage>{meta.FeaturedImageUrl}</featuredImage>
<keywords>
{meta.Keywords}
</keywords>
<weblogs>
<postid>{meta.PostId}</postid>
<weblog>
{meta.WeblogName}
</weblog>
</weblogs>
</blogpost>
```
-->
<!-- End Post Configuration -->
";

            if (WeblogAddinConfiguration.Current.AddFrontMatterToNewBlogPost)
            {

                post = String.Format(WeblogAddinConfiguration.Current.FrontMatterTemplate,
                meta.Title, DateTime.Now) + 
                post;
            }

            return post;
        }

        public void CreateNewPostOnDisk(string title, string postFilename, string weblogName)
        {
            string filename = mmFileUtils.SafeFilename(postFilename);
            string titleFilename = mmFileUtils.SafeFilename(title);

            var folder = Path.Combine(WeblogAddinConfiguration.Current.PostsFolder,DateTime.Now.Year + "-" + DateTime.Now.Month.ToString("00"), titleFilename.Replace(" ","-"));
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            var outputFile = Path.Combine(folder, filename);

            // Create the new post by creating a file with title preset
            string newPostMarkdown = NewWeblogPost(new WeblogPostMetadata()
            {
                Title = title,
                WeblogName = weblogName
            });
            File.WriteAllText(outputFile, newPostMarkdown);
            Model.Window.OpenTab(outputFile);

            mmApp.Configuration.LastFolder = Path.GetDirectoryName(outputFile);
        }

        /// <summary>
        /// determines whether post is a new post based on
        /// a postId of various types
        /// </summary>
        /// <param name="postId">Integer or String or null</param>
        /// <returns></returns>
        bool IsNewPost(object postId)
        {
            if (postId == null)
                return true;

            if (postId is string)
                return string.IsNullOrEmpty((string) postId);

            if (postId is int && (int)postId < 1)
                return true;

            return false;
        }

        /// <summary>
        /// Adds a post id to Weblog configuration in a weblog post document.
        /// Only works if [categories] key exists.
        /// </summary>
        /// <param name="markdown"></param>
        /// <param name="postId"></param>
        /// <returns></returns>
        public string AddPostId(string markdown, int postId)
        {
            markdown = markdown.Replace("</categories>",
                    "</categories>\r\n" +
                    "<postid>" + ActivePost.PostID + "</postid>");

            return markdown;
        }

#endregion

#region Post Configuration and Meta Data

        /// <summary>
        /// Strips the Markdown Meta data from the message and populates
        /// the post structure with the meta data values.
        /// </summary>
        /// <param name="markdown"></param>
        /// <param name="weblogInfo"></param>
        /// <returns></returns>
        public WeblogPostMetadata GetPostConfigFromMarkdown(string markdown, WeblogInfo weblogInfo)
        {
            var meta = new WeblogPostMetadata()
            {
                RawMarkdownBody = markdown,
                MarkdownBody = markdown,
                WeblogName = WeblogAddinConfiguration.Current.LastWeblogAccessed,
                CustomFields = new Dictionary<string, string>()
            };
            
            // check for title in first line and remove it 
            // since the body shouldn't render the title
            var lines = StringUtils.GetLines(markdown,20);
            if (lines.Length > 0 && lines[0].StartsWith("# "))
            {
                if (weblogInfo.Type != WeblogTypes.Medium) // medium wants the header in the text
                    meta.MarkdownBody = meta.MarkdownBody.Replace(lines[0], "").Trim();                

                meta.Title = lines[0].Trim().Substring(2);
            }
            else if (lines.Length > 2 && lines[0] == "---" && meta.MarkdownBody.Contains("layout: post"))
            {
                var block = mmFileUtils.ExtractString(meta.MarkdownBody, "---", "---", returnDelimiters: true);
                if (!string.IsNullOrEmpty(block))
                {
                    meta.Title = StringUtils.ExtractString(block, "title: ", "\n").Trim();
                    meta.MarkdownBody = meta.MarkdownBody.Replace(block, "").Trim();
                }
            }


            string config = StringUtils.ExtractString(markdown,
                "<!-- Post Configuration -->",
                "<!-- End Post Configuration -->",
                caseSensitive: false, allowMissingEndDelimiter: true, returnDelimiters: true);

            if (string.IsNullOrEmpty(config))
                return meta;

            // strip the config section
            meta.MarkdownBody = meta.MarkdownBody.Replace(config, "");


            string title = StringUtils.ExtractString(config, "\n<title>", "</title>").Trim();
            if (string.IsNullOrEmpty(meta.Title))
                meta.Title = title;
            meta.Abstract = StringUtils.ExtractString(config, "\n<abstract>", "\n</abstract>").Trim();
            meta.Keywords = StringUtils.ExtractString(config, "\n<keywords>", "\n</keywords>").Trim();
            meta.Categories = StringUtils.ExtractString(config, "\n<categories>", "\n</categories>").Trim();
            meta.PostId = StringUtils.ExtractString(config, "\n<postid>", "</postid>").Trim();
            string strIsDraft = StringUtils.ExtractString(config, "\n<isDraft>", "</isDraft>").Trim();
            if (strIsDraft != null && strIsDraft == "True")
                meta.IsDraft = true;
            string weblogName = StringUtils.ExtractString(config, "\n<weblog>", "</weblog>").Trim();
            if (!string.IsNullOrEmpty(weblogName))
                meta.WeblogName = weblogName;

            string featuredImageUrl = StringUtils.ExtractString(config, "\n<featuredImage>", "</featuredImage>");
            if (!string.IsNullOrEmpty(featuredImageUrl))
                meta.FeaturedImageUrl = featuredImageUrl.Trim();

            

            string customFieldsString = StringUtils.ExtractString(config, "\n<customFields>", "</customFields>",returnDelimiters: true);
            if (!string.IsNullOrEmpty(customFieldsString))
            {

                try
                {
                    var dom = new XmlDocument();
                    dom.LoadXml(customFieldsString);

                    foreach (XmlNode child in dom.DocumentElement.ChildNodes)
                    {
                        if (child.NodeType == XmlNodeType.Element)
                        {
                            var key = child.FirstChild.InnerText;
                            var value = child.ChildNodes[1].InnerText;                            
                            meta.CustomFields.Add(key, value);                            
                        }
                    }
                }
                catch { }
            }
            
            ActivePost.Title = meta.Title;            
            ActivePost.Categories = meta.Categories.Split(new [] { ','},StringSplitOptions.RemoveEmptyEntries);
            ActivePost.Tags = meta.Keywords.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            ActivePost.mt_excerpt = meta.Abstract;
            ActivePost.mt_keywords = meta.Keywords;

           

            return meta;
        }

        /// <summary>
        /// This method sets the RawMarkdownBody
        /// </summary>
        /// <param name="meta"></param>
        /// <returns>Updated Markdown - also sets the RawMarkdownBody and MarkdownBody</returns>
        public string SetConfigInMarkdown(WeblogPostMetadata meta)
        {
            string markdown = meta.RawMarkdownBody;


            string customFields = null;
            if (meta.CustomFields != null && meta.CustomFields.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine("<customFields>");
                foreach (var cf in meta.CustomFields)
                {
                    sb.AppendLine("\t<customField>");
                    sb.AppendLine($"\t\t<key>{cf.Key}</key>");
                    sb.AppendLine($"\t\t<value>{System.Net.WebUtility.HtmlEncode(cf.Value)}</value>");
                    sb.AppendLine("\t</customField>");
                }
                sb.AppendLine("</customFields>");
                customFields = sb.ToString();
            }

            string origConfig = StringUtils.ExtractString(markdown, "<!-- Post Configuration -->", "<!-- End Post Configuration -->", false, false, true);
            string newConfig = $@"<!-- Post Configuration -->
<!--
```xml
<blogpost>
<title>{meta.Title}</title>
<abstract>
{meta.Abstract}
</abstract>
<categories>
{meta.Categories}
</categories>
<keywords>
{meta.Keywords}
</keywords>
<isDraft>{meta.IsDraft}</isDraft>
<featuredImage>{meta.FeaturedImageUrl}</featuredImage>{customFields}
<weblogs>
<postid>{meta.PostId}</postid>
<weblog>
{meta.WeblogName}
</weblog>
</weblogs>
</blogpost>
```
-->
<!-- End Post Configuration -->";

            if (string.IsNullOrEmpty(origConfig))
            {
                markdown += "\r\n" + newConfig;
            }
            else
                markdown = markdown.Replace(origConfig, newConfig);

            meta.RawMarkdownBody = markdown;
            meta.MarkdownBody = meta.RawMarkdownBody.Replace(newConfig, "");

            return markdown;
        }

        //public string SafeFilename(string fileName, string replace = "")
        //{
        //    string filename = Path.GetInvalidFileNameChars()
        //        .Aggregate(fileName,
        //            (current, c) => current.Replace(c.ToString(), replace));

        //    filename = filename.Replace("#", "");
        //    return filename;
        //}

#endregion

#region Downloaded Post Handling

        public void CreateDownloadedPostOnDisk(Post post, string weblogName)
        {
            string filename = mmFileUtils.SafeFilename(post.Title);

            var folder = Path.Combine(WeblogAddinConfiguration.Current.PostsFolder,
                "Downloaded",weblogName,
                filename);

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            var outputFile = Path.Combine(folder, StringUtils.ToCamelCase(filename) + ".md");


            bool isMarkdown = false;
            string body = post.Body;
            string featuredImage = null;

            if (post.CustomFields != null)
            {
                var cf = post.CustomFields.FirstOrDefault(custf => custf.ID == "mt_markdown");
                if (cf != null)
                {
                    body = cf.Value;
                    isMarkdown = true;
                }

                cf = post.CustomFields.FirstOrDefault(custf => custf.ID == "wp_post_thumbnail");
                if (cf != null)
                    featuredImage = cf.Value;
            }
            if (!isMarkdown)
            {                
                if (!string.IsNullOrEmpty(post.mt_text_more))
                {
                    // Wordpress ReadMore syntax - SERIOUSLY???
                    if (string.IsNullOrEmpty(post.mt_excerpt))                    
                        post.mt_excerpt = HtmlUtils.StripHtml(post.Body);                     
                    
                    body = MarkdownUtilities.HtmlToMarkdown(body) +
                            "\n\n<!--more-->\n\n" +
                            MarkdownUtilities.HtmlToMarkdown(post.mt_text_more);                    
                }
                else
                    body = MarkdownUtilities.HtmlToMarkdown(body);

            }
            
            string categories = null;
            if (post.Categories != null && post.Categories.Length > 0)
                categories = string.Join(",", post.Categories);


            // Create the new post by creating a file with title preset
            string newPostMarkdown = NewWeblogPost(new WeblogPostMetadata()
            {
                Title = post.Title,
                MarkdownBody = body,
                Categories = categories,
                Keywords = post.mt_keywords,
                Abstract = post.mt_excerpt,
                PostId = post.PostID.ToString(),
                WeblogName = weblogName,
                FeaturedImageUrl = featuredImage         
            });
            File.WriteAllText(outputFile, newPostMarkdown);
            
            mmApp.Configuration.LastFolder = Path.GetDirectoryName(outputFile);

            if (isMarkdown)
            {
                string html = post.Body;
                string path = mmApp.Configuration.LastFolder;

                // do this synchronously so images show up :-<
                ShowStatus("Downloading post images...");
                SaveMarkdownImages(html, path);
                ShowStatus("Post download complete.", 5000);

                //new Action<string,string>(SaveImages).BeginInvoke(html,path,null, null);
            }

            Model.Window.OpenTab(outputFile);
        }

        private void SaveMarkdownImages(string htmlText, string basePath)
        {
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlText);

                // send up normalized path images as separate media items
                var images = doc.DocumentNode.SelectNodes("//img");
                if (images != null)
                {
                    foreach (HtmlNode img in images)
                    {
                        string imgFile = img.Attributes["src"]?.Value;
                        if (imgFile == null)
                            continue;

                        if (imgFile.StartsWith("http://") || imgFile.StartsWith("https://"))
                        {
                            string imageDownloadPath = Path.Combine(basePath, Path.GetFileName(imgFile));

                            try
                            {
                                var http = new HttpUtilsWebClient();
                                http.DownloadFile(imgFile, imageDownloadPath);
                            }
                            catch // just continue on errorrs
                            { }
                        }
                    }
                }
            }
            catch // catch so thread doesn't crash
            {
            }
        }

#endregion
    }
}
