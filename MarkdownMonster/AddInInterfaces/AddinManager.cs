﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FontAwesome.WPF;
using MarkdownMonster.Annotations;
using Westwind.Utilities;

namespace MarkdownMonster.AddIns
{
    /// <summary>
    /// This class manages loading of addins and 
    /// raising various application events passed
    /// to all addins that they can respond to
    /// </summary>
    public class AddinManager
    {
        /// <summary>
        /// Singleton to get access to Addin Manager
        /// </summary>
        public static AddinManager Current { get; set; }

        /// <summary>
        /// The full list of add ins registered
        /// </summary>
        public List<MarkdownMonsterAddin> AddIns;

        public string ErrorMessage { get; set; }

        static AddinManager()
        {
            Current = new AddinManager();
        }

        public AddinManager()
        {
            AddIns = new List<MarkdownMonsterAddin>();
        }

        /// <summary>
        /// Loads add-ins into the application from the add-ins folder
        /// </summary>
        internal void LoadAddins()
        {
            string addinPath = Path.Combine(Environment.CurrentDirectory, "AddIns");
            if (!Directory.Exists(addinPath))
                return;

            // Clear out old addin files in root
            // TODO: Remove after a few months
            try
            {
                var files = Directory.GetFiles(addinPath);
                foreach (var file in files)                
                    File.Delete(file);
            } catch { }
            

            // Check for Addins to install
            try
            {
                if (Directory.Exists(addinPath + "\\Install"))
                    InstallAddinFiles(addinPath + "\\Install");
            }
            catch (Exception ex)
            {
                mmApp.Log($"Addin Update failed: {ex.Message}");
            }

            var dirs = Directory.GetDirectories(addinPath);            
            foreach (var dir in dirs)
            {
                var files = Directory.GetFiles(dir, "*.dll");
                foreach (var file in files)
                {
                    string fname = Path.GetFileName(file).ToLower();
                    if (fname.EndsWith("addin.dll"))
                        LoadAddinClasses(file);
                }
            }
        }

        

        /// <summary>
        /// Load all add in classes in an assembly
        /// </summary>
        /// <param name="assemblyFile"></param>
        private void LoadAddinClasses(string assemblyFile)
        {
            Assembly asm = null;
            Type[] types = null;

            try
            {
                asm = Assembly.LoadFrom(assemblyFile);
                string path = Path.GetDirectoryName(assemblyFile);

                var assemblies = AppDomain.CurrentDomain.GetAssemblies();

                // load dependencies
                foreach (var refAssembly in asm.GetReferencedAssemblies())
                {
                    // already loaded?
                    Assembly assembly = assemblies.FirstOrDefault(a => a.FullName == refAssembly.FullName);
                    if (assembly != null)
                        continue;
                    
                    // see if there's a local file in same folder as addin
                    string file = Path.Combine(path, refAssembly.FullName.Split(',')[0] + ".dll");
                    if (File.Exists(file))
                        Assembly.LoadFrom(file);
                    else
                        // load from Gac
                        Assembly.Load(refAssembly.FullName);
                }

                types = asm.GetTypes();
            }
            catch(Exception ex)
            {
                MessageBox.Show("Unable to load add-in assembly: " + Path.GetFileNameWithoutExtension(assemblyFile));
                return;
            }

            foreach (var type in types)
            {
                var typeList = type.FindInterfaces(AddinInterfaceFilter, typeof(IMarkdownMonsterAddin));
                if (typeList.Length > 0)
                {
                    var ai = Activator.CreateInstance(type) as MarkdownMonsterAddin;
                    this.AddIns.Add(ai);
                }
            }
        }


        private void InstallAddinFiles(string path)
        {
            throw new NotImplementedException();

            //var addinBasePath = Path.GetFullPath(Path.Combine(path, ".."));

            
            //var dirs = Directory.GetDirectories(path);
            //foreach (var addinInstallFolder in dirs)
            //{
            //    var files = Directory.GetFiles(addinInstallFolder);
            //    foreach (var file in files)
            //    {
            //        var filename = Path.GetFileName(file);
            //        File.Copy(file, Path.Combine(addinBase, filename), true);
            //        File.Delete(file);
            //    }
            //    foreach (var dir in dirs)
            //    {
            //        var files2 = Directory.GetFiles(Path.Combine(path, dir));
            //        foreach (var file in files2)
            //        {
            //            File.Copy(file, Path.Combine(addinBasePath, dir, Path.GetFileName(file)), true);
            //            File.Delete(file);
            //        }
            //    }

            //    Directory.Delete(path, true);
            //}
        }


        private static bool AddinInterfaceFilter(Type typeObj, Object criteriaObj)
        {
            if (typeObj.ToString() == criteriaObj.ToString())
                return true;
            else
                return false;
        }


        /// <summary>
        /// Loads the add-in menu and toolbar buttons
        /// </summary>
        /// <param name="window"></param>
        public void InitializeAddinsUi(MainWindow window)
        {
            foreach (var addin in AddIns)
            {
                addin.Model = window.Model;


                foreach (var menuItem in addin.MenuItems)
                {
                    var mitem = new MenuItem()
                    {
                        Header = menuItem.Caption

                    };
                    if (menuItem.CanExecute == null)
                        mitem.Command = new CommandBase((s, c) => menuItem.Execute?.Invoke(mitem));
                    else
                        mitem.Command = new CommandBase((s, c) => menuItem.Execute.Invoke(mitem),
                                                        (s, c) => menuItem.CanExecute.Invoke(mitem));

                    addin.Model.Window.MenuAddins.Items.Add(mitem);

                    // if an icon is provided also add to toolbar
                    if (menuItem.FontawesomeIcon != FontAwesomeIcon.None)
                    {
                        var hasConfigMenu = menuItem.ExecuteConfiguration != null;

                        var titem = new Button();
                        titem.Content = new Image()
                        {
                            Source =
                                ImageAwesome.CreateImageSource(menuItem.FontawesomeIcon, addin.Model.Window.Foreground),
                            ToolTip = menuItem.Caption,
                            Height = 16,
                            Width = 16,
                            Margin = new Thickness(5, 0, hasConfigMenu ? 0 : 5, 0)
                        };

                        if (menuItem.Execute != null)
                        {
                            if (menuItem.CanExecute == null)
                                titem.Command = new CommandBase((s, c) => menuItem.Execute?.Invoke(titem));
                            else
                                titem.Command = new CommandBase((s, c) => menuItem.Execute.Invoke(titem),
                                                                (s, c) => menuItem.CanExecute.Invoke(titem));
                        }

                        addin.Model.Window.ToolbarAddIns.Visibility = System.Windows.Visibility.Visible;
                        addin.Model.Window.ToolbarAddIns.Items.Add(titem);

                        // Add configuration dropdown if configured
                        if (hasConfigMenu)
                        {
                            var tcitem = new Button
                            {
                                FontSize = 10F,
                                Content = new Image()
                                {
                                    Source =
                                        ImageAwesome.CreateImageSource(FontAwesomeIcon.CaretDown,
                                            addin.Model.Window.Foreground),
                                    ToolTip = menuItem.Caption + " Configuration",
                                    Height = 16,
                                    Width = 8,
                                    Margin = new Thickness(0, 0, 0, 0),
                                }
                            };

                            if (menuItem.CanExecute == null)
                                tcitem.Command = new CommandBase((sender, c) => menuItem.ExecuteConfiguration.Invoke(sender));
                            else
                                tcitem.Command = new CommandBase((sender, c) => menuItem.ExecuteConfiguration.Invoke(sender),
                                                                 (s, c) => menuItem.CanExecute.Invoke(titem));

                            addin.Model.Window.ToolbarAddIns.Items.Add(tcitem);
                        }
                    }
                }
            }
        }

        public void RaiseOnApplicationStart()
        {
            foreach (var addin in AddIns)
            {
                try
                {
                    addin?.OnApplicationStart();
                }
                catch (Exception ex)
                {
                    mmApp.Log(addin.Id + "::AddIn::OnApplicationStart Error: " + ex.GetBaseException().Message);
                }
            }
        }

        public void RaiseOnApplicationShutdown()
        {
            foreach (var addin in AddIns)
            {
                try
                {
                    addin?.OnApplicationShutdown();
                }
                catch (Exception ex)
                {

                    mmApp.Log(addin.Id + "::AddIn::OnApplicationShutdown Error: " + ex.GetBaseException().Message);
                }

            }
        }

        public bool RaiseOnBeforeOpenDocument(string filename)
        {
            foreach (var addin in AddIns)
            {
                if (addin == null)
                    continue;
                try
                {
                    if (!addin.OnBeforeOpenDocument(filename))
                        return false;
                }
                catch (Exception ex)
                {

                    mmApp.Log(addin.Id + "::AddIn::OnBeforeOpenDocument Error: " + ex.GetBaseException().Message);
                }
            }

            return true;
        }


        public void RaiseOnAfterOpenDocument(MarkdownDocument doc)
        {
            foreach (var addin in AddIns)
            {
                try
                {
                    addin?.OnAfterOpenDocument(doc);
                }
                catch (Exception ex)
                {
                    mmApp.Log(addin.Id + "::AddIn::nAfterOpenDocument Error: " + ex.GetBaseException().Message);
                }
            }
        }

        public bool RaiseOnBeforeSaveDocument(MarkdownDocument doc)
        {
            foreach (var addin in AddIns)
            {
                if (addin == null)
                    continue;
                try
                {
                    if (!addin.OnBeforeSaveDocument(doc))
                        return false;
                }
                catch (Exception ex)
                {
                    mmApp.Log(addin.Id + "::AddIn::OnBeforeSaveDocument Error: " + ex.GetBaseException().Message);
                }
            }

            return true;
        }


        public void RaiseOnAfterSaveDocument(MarkdownDocument doc)
        {
            foreach (var addin in AddIns)
            {
                try
                {
                    addin?.OnAfterSaveDocument(doc);
                }
                catch (Exception ex)
                {
                    mmApp.Log(addin.Id + "::AddIn::OnAfterSaveDocument Error: " + ex.GetBaseException().Message);
                }
            }
        }

        public string RaiseOnSaveImage(object image)
        {
            string url = null;

            foreach (var addin in AddIns)
            {
                try
                {
                    url = addin?.OnSaveImage(image);
                }
                catch (Exception ex)
                {
                    mmApp.Log(addin.Id + "::AddIn::OnAfterSaveDocument Error: " + ex.GetBaseException().Message);
                }
            }

            return url;
        }

        public void RaiseOnDocumentActivated(MarkdownDocument doc)
        {
            foreach (var addin in AddIns)
            {
                try
                {
                    addin?.OnDocumentActivated(doc);
                }
                catch (Exception ex)
                {
                    mmApp.Log(addin.Id + "::AddIn::OnDocumentActivated Error: " + ex.GetBaseException().Message);
                }
            }
        }

        public void RaiseOnNotifyAddin(string command, object parameter)
        {
            foreach (var addin in AddIns)
            {
                try
                {
                    addin?.OnNotifyAddin(command, parameter);
                }
                catch (Exception ex)
                {
                    mmApp.Log(addin.Id + "::AddIn::OnNotifyAddin Error: " + ex.GetBaseException().Message);
                }
            }
        }

        public string RaiseOnEditorCommand(string action, string input)
        {
            foreach (var addin in AddIns)
            {
                try
                {
                    string html = addin?.OnEditorCommand(action, input);
                    if (string.IsNullOrEmpty(html))
                        return html;
                }
                catch (Exception ex)
                {
                    mmApp.Log(addin.Id + "::AddIn::OnDocumentActivated Error: " + ex.GetBaseException().Message);
                }
            }

            return null;
        }

        #region Addin Manager
        public List<AddinItem> GetAddinList()
        {
            const string addinListRepoUrl =
                "https://raw.githubusercontent.com/RickStrahl/MarkdownMonsterAddinsRegistry/master/MarkdownMonsterAddinRegistry.json";

            var settings = new HttpRequestSettings
            {
                Url = addinListRepoUrl,
                Timeout = 5000
            };

            List<AddinItem> addinList;
            try
            {
                addinList = HttpUtils.JsonRequest<List<AddinItem>>(settings);
            }
            catch (Exception ex)
            {
                this.ErrorMessage = ex.Message;
                return null;
            }

            addinList
                .AsParallel()
                .ForAll(ai =>
                {
                    try
                    {
                        var dl = HttpUtils.JsonRequest<AddinItem>(new HttpRequestSettings
                        {
                            Url = ai.gitVersionUrl
                        });
                        DataUtils.CopyObjectData(dl, ai, "id,name,gitVersionUrl,gitUrl");
                    }
                    catch { /* ignore error */}
                });

            return addinList;
        }

        public async Task<List<AddinItem>> GetAddinListAsync()
        {
            const string addinListRepoUrl =
                "https://raw.githubusercontent.com/RickStrahl/MarkdownMonsterAddinsRegistry/master/MarkdownMonsterAddinRegistry.json";

            var settings = new HttpRequestSettings
            {
                Url = addinListRepoUrl,
                Timeout = 5000
            };

            List<AddinItem> addinList;
            try
            {
                addinList = await HttpUtils.JsonRequestAsync<List<AddinItem>>(settings);
            }
            catch (Exception ex)
            {
                this.ErrorMessage = ex.Message;
                return null;
            }

            addinList
                .AsParallel()
                .ForAll(async ai =>
                {
                    try
                    {
                        var dl = await HttpUtils.JsonRequestAsync<AddinItem>(new HttpRequestSettings
                        {
                            Url = ai.gitVersionUrl
                        });
                        DataUtils.CopyObjectData(dl, ai, "id,name,gitVersionUrl,gitUrl");

                        ai.icon = ai.gitVersionUrl.Replace("Version.json", ai.icon);
                    }
                    catch(Exception ex)
                    {
                        mmApp.Log($"Addin {ai.name} version failed", ex);                        
                    }
                });

            return addinList;
        }

        /// <summary>
        /// This downloads the addin and temporarily dumps it into the 
        /// addins/install folder. 
        /// 
        /// The addin-loader then moves the files.
        /// </summary>
        /// <param name="url"></param>
        /// <param name="targetFolder"></param>
        /// <returns></returns>
        public bool DownloadAndInstallAddin(string url, string targetFolder = null)
        {
            if (string.IsNullOrEmpty(targetFolder))
                targetFolder = Path.GetFullPath(".\\Addins\\Install\\");

            string file = Path.GetTempFileName();
            file = Path.ChangeExtension(file, "zip");

            try
            {
                using (WebClient client = new WebClient())
                {
                    client.DownloadFile(url, file);
                }

                using (ZipArchive archive = ZipFile.OpenRead(file))
                {
                    foreach (ZipArchiveEntry zipfile in archive.Entries)
                    {
                        string fullName = Path.Combine(targetFolder, zipfile.FullName);

                        //Extracts the files to the output folder in a safer manner
                        if (!File.Exists(fullName))
                        {
                            //Calculates what the new full path for the unzipped file should be
                            string fullPath = Path.GetDirectoryName(fullName);

                            //Creates the directory (if it doesn't exist) for the new path
                            Directory.CreateDirectory(fullPath);

                            //Extracts the file to (potentially new) path
                            zipfile.ExtractToFile(fullName, true);
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                this.ErrorMessage = ex.Message;
                return false;
            }

        }
        #endregion


    }


    public class AddinItem : INotifyPropertyChanged
    {        
        public string id
        {
            get { return _id; }
            set
            {
                if (value == _id) return;
                _id = value;
                OnPropertyChanged();
            }
        }
        private string _id;

        public string gitUrl
        {
            get { return _gitUrl; }
            set
            {
                if (value == _gitUrl) return;
                _gitUrl = value;
                OnPropertyChanged();
            }
        }
        private string _gitUrl;

        public string gitVersionUrl
        {
            get { return _gitVersionUrl; }
            set
            {
                if (value == _gitVersionUrl) return;
                _gitVersionUrl = value;
                OnPropertyChanged();
            }
        }
        private string _gitVersionUrl;
        

        public string downloadUrl
        {
            get { return _downloadUrl; }
            set
            {
                if (value == _downloadUrl) return;
                _downloadUrl = value;
                OnPropertyChanged();
            }
        }
        private string _downloadUrl;


        public string name
        {
            get { return _name; }
            set
            {
                if (value == _name) return;
                _name = value;
                OnPropertyChanged();
            }
        }
        private string _name;

        public string summary
        {
            get { return _summary; }
            set
            {
                if (value == _summary) return;
                _summary = value;
                OnPropertyChanged();
            }
        }
        private string _summary;

        
        public string description
        {
            get { return _description; }
            set
            {
                if (value == _description) return;
                _description = value;
                OnPropertyChanged();
            }
        }
        private string _description;


        public string version
        {
            get { return _version; }
            set
            {
                if (value == _version) return;
                _version = value;
                OnPropertyChanged();
            }
        }
        private string _version;

        public string author
        {
            get { return _author; }
            set
            {
                if (value == _author) return;
                _author = value;
                OnPropertyChanged();
            }
        }
        private string _author;

        

        public string icon
        {
            get { return _icon; }
            set
            {
                if (value == _icon) return;
                _icon = value;
                OnPropertyChanged();
            }
        }
        private string _icon;

        

        public DateTime updated
        {
            get { return _updated; }
            set
            {
                if (value.Equals(_updated)) return;
                _updated = value;
                OnPropertyChanged();
            }
        }
        private DateTime _updated;

        

        public bool IsInstalled
        {
            get { return _isInstalled; }
            set
            {
                if (value == _isInstalled) return;
                _isInstalled = value;
                OnPropertyChanged();
            }
        }
        private bool _isInstalled;

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
