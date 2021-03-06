﻿#region XbimHeader

// The eXtensible Building Information Modelling (xBIM) Toolkit
// Solution:    XbimComplete
// Project:     XbimXplorer
// Filename:    XplorerMainWindow.xaml.cs
// Published:   01, 2012
// Last Edited: 9:05 AM on 20 12 2011
// (See accompanying copyright.rtf)

#endregion

#region Directives

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using log4net;
using log4net.Repository.Hierarchy;
using Microsoft.Win32;
using Xbim.IO;
using Xbim.ModelGeometry.Scene;
using Xbim.Presentation;
using Xbim.Presentation.FederatedModel;
using Xbim.Presentation.LayerStyling;
using Xbim.Presentation.LayerStylingV2;
using Xbim.Presentation.ModelGeomInfo;
using Xbim.Presentation.XplorerPluginSystem;
using Xbim.XbimExtensions;
using Xbim.XbimExtensions.Interfaces;
using XbimGeometry.Interfaces;
using XbimXplorer.Dialogs;
using XbimXplorer.LogViewer;
using XbimXplorer.Properties;
using XbimXplorer.Querying;
using Xceed.Wpf.AvalonDock.Layout.Serialization;

#endregion

namespace XbimXplorer
{
    /// <summary>
    ///   Interaction logic for Window1.xaml
    /// </summary>
    public partial class XplorerMainWindow : IXbimXplorerPluginMasterWindow, INotifyPropertyChanged
    {
        private static readonly ILog Log = LogManager.GetLogger("Xbim.WinUI");

        public Visibility AnyErrors
        {
            get
            {
                return NumErrors > 0 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
        }

        public int NumErrors { get; private set; }

        public Visibility AnyWarnings
        {
            get
            {
                return NumWarnings > 0 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
        }

        public int NumWarnings { get; private set; }


        private BackgroundWorker _worker;
        /// <summary>
        /// 
        /// </summary>
        public static RoutedCommand CreateFederationCmd = new RoutedCommand();
        /// <summary>
        /// 
        /// </summary>
        public static RoutedCommand EditFederationCmd = new RoutedCommand();
        /// <summary>
        /// 
        /// </summary>
        public static RoutedCommand OpenFederationCmd = new RoutedCommand();


        /// <summary>
        /// 
        /// </summary>
        public static RoutedCommand OpenExportWindowCmd = new RoutedCommand();
        /// <summary>
        /// 
        /// </summary>
        public static RoutedCommand ExportCoBieCmd = new RoutedCommand();
        /// <summary>
        /// 
        /// </summary>
        public static RoutedCommand CoBieClassFilter = new RoutedCommand();
        
        private string _temporaryXbimFileName;

        private string _openedModelFileName;

        /// <summary>
        /// Deals with the user-defined model file name.
        /// The underlying XbimModel might be pointing to a temporary file elsewhere.
        /// </summary>
        /// <returns>String pointing to the file or null if the file is not defined (e.g. not saved federation).</returns>
        public string GetOpenedModelFileName()
        {
            return _openedModelFileName;
        }

        private void SetOpenedModelFileName(string ifcFilename)
        {
            _openedModelFileName = ifcFilename;

            Dispatcher.BeginInvoke(new Action(delegate
            {
                // Do your work
                Title = string.IsNullOrEmpty(ifcFilename)
                    ? "Xbim Xplorer" :
                    "Xbim Xplorer - [" + ifcFilename + "]";
            }));
           
        }
        
        private EventAppender _appender;

        private bool _blockPlugin;

        public XplorerMainWindow(bool blockPlugin = false)
        {
            InitializeComponent();
            _blockPlugin = blockPlugin;

            // initialise the internal elements of the UI that behave like plugins
            EvaluateXbimUiType(typeof(LogViewer.LogViewer));
            EvaluateXbimUiType(typeof(WdwQuery));

            Closed += XplorerMainWindow_Closed;
            Loaded += XplorerMainWindow_Loaded;
            Closing += XplorerMainWindow_Closing;
            DrawingControl.UserModeledDimensionChangedEvent += DrawingControl_MeasureChangedEvent;
            InitFromSettings();
            RefreshRecentFiles();

            // logging 
            LoggedEvents = new ObservableCollection<EventViewModel>();
        }

        public ObservableCollection<EventViewModel> LoggedEvents { get; private set; }

        internal void appender_Logged(object sender, LogEventArgs e)
        {
            foreach (var loggingEvent in e.LoggingEvents)
            {
                var m = new EventViewModel(loggingEvent);
                Application.Current.Dispatcher.BeginInvoke((Action) delegate {
                    LoggedEvents.Add(m);
                });
                Application.Current.Dispatcher.BeginInvoke((Action) UpdateLoggerCounts);
            }
        }

        internal void UpdateLoggerCounts()
        {
            NumErrors = 0;
            NumWarnings = 0;
            foreach (var loggedEvent in LoggedEvents)
            {
                switch (loggedEvent.Level)
                {
                    case "ERROR":
                        NumErrors++;
                        break;
                    case "WARN":
                        NumWarnings++;
                        break;
                }
            }
            OnPropertyChanged("AnyErrors");
            OnPropertyChanged("NumErrors");
            OnPropertyChanged("AnyWarnings");
            OnPropertyChanged("NumWarnings");
        }

        public Visibility DeveloperVisible
        {
            get {
                return Settings.Default.DeveloperMode 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
        }
            

        private void InitFromSettings()
        {
            _fileAccessMode = Settings.Default.FileAccessMode;
            OnPropertyChanged("DeveloperVisible");
            
        }

        private ObservableMruList<string> _mruFiles = new ObservableMruList<string>();
        private void RefreshRecentFiles()
        {
            var s = new List<string>();
            if (Settings.Default.MRUFiles != null)
                s.AddRange(Settings.Default.MRUFiles.Cast<string>());

            _mruFiles = new ObservableMruList<string>(s, 4, StringComparer.InvariantCultureIgnoreCase);
            MnuRecent.ItemsSource = _mruFiles;
        }

        private void AddRecentFile()
        {
            _mruFiles.Add(_openedModelFileName);
            Settings.Default.MRUFiles = new StringCollection();
            foreach (var item in _mruFiles)
            {
                Settings.Default.MRUFiles.Add(item);
            }
            Settings.Default.Save();
        }

        private void DrawingControl_MeasureChangedEvent(DrawingControl3D m, PolylineGeomInfo e)
        {
            if (e != null)
            {
                EntityLabel.Text = e.ToString();
                Debug.WriteLine("Points:");
                foreach (var pt in e.VisualPoints)
                {
                    Debug.WriteLine("X:{0} Y:{1} Z:{2}", pt.X, pt.Y, pt.Z);
                }
            }
        }
        
        #region "Model File Operations"

        void XplorerMainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_worker != null && _worker.IsBusy)
                e.Cancel = true; //do nothing if a thread is alive
            else
                e.Cancel = false;

        }

        void XplorerMainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var model = XbimModel.CreateTemporaryModel();
            model.Initialise();
            ModelProvider.ObjectInstance = model;
            ModelProvider.Refresh();

            // logging information warnings
            _appender = new EventAppender {Tag = "MainWindow"};
            _appender.Logged += appender_Logged;

            var hier = LogManager.GetRepository() as Hierarchy;
            if (hier != null)
                hier.Root.AddAppender(_appender);

        }

        void XplorerMainWindow_Closed(object sender, EventArgs e)
        {
            CloseAndDeleteTemporaryFiles();
        }


        private void OpenIfcFile(object s, DoWorkEventArgs args)
        {
            var worker = s as BackgroundWorker;
            var ifcFilename = args.Argument as string;

            var model = new XbimModel();
            try
            {
                _temporaryXbimFileName = Path.GetTempFileName();
                SetOpenedModelFileName(ifcFilename);
                

                if (worker != null)
                {
                    model.CreateFrom(ifcFilename, _temporaryXbimFileName, worker.ReportProgress, true);
                    var context = new Xbim3DModelContext(model);//upgrade to new geometry represenation, uses the default 3D model
                    context.CreateContext(geomStorageType: XbimGeometryType.PolyhedronBinary,  progDelegate: worker.ReportProgress,  adjustWCS: false);
            
                    if (worker.CancellationPending) //if a cancellation has been requested then don't open the resulting file
                    {
                        try
                        {
                            model.Close();
                            if (File.Exists(_temporaryXbimFileName))
                                File.Delete(_temporaryXbimFileName); //tidy up;
                            _temporaryXbimFileName = null;
                            SetOpenedModelFileName(null);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex.Message, ex);
                        }
                        return;
                    }
                }
                args.Result = model;
            }
            catch (Exception ex)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Error reading " + ifcFilename);
                var indent = "\t";
                while (ex != null)
                {
                    sb.AppendLine(indent + ex.Message);
                    ex = ex.InnerException;
                    indent += "\t";
                }

                args.Result = new Exception(sb.ToString());
            }
        }

      

        XbimDBAccess _fileAccessMode = XbimDBAccess.Read;
        /// <summary>
        /// 
        /// </summary>
        public XbimDBAccess FileAccessMode
        {
            get { return _fileAccessMode; }
            set { _fileAccessMode = value; }
        }

        /// <summary>
        ///   This is called when we explcitly want to open an xBIM file
        /// </summary>
        /// <param name = "s"></param>
        /// <param name = "args"></param>
        private void OpenXbimFile(object s, DoWorkEventArgs args)
        {
            var worker = s as BackgroundWorker;
            var fileName = args.Argument as string;
            var model = new XbimModel();
            try
            {
                var dbAccessMode = _fileAccessMode;
                if (worker != null)
                {
                    model.Open(fileName, dbAccessMode, worker.ReportProgress); //load entities into the model

                    if (model.IsFederation)
                    {
                        // needs to open the federation in rw mode
                        if (dbAccessMode != XbimDBAccess.ReadWrite)
                        {
                            model.Close();
                            model.Open(fileName, XbimDBAccess.ReadWrite, worker.ReportProgress); // federations need to be opened in read/write for the editor to work
                        }

                        // sets a convenient integer to all children for model identification
                        // this is used by the federated model selection mechanisms.
                        var i = 0;
                        foreach (var item in model.AllModels)
                        {
                            item.Tag = i++;
                        }
                    }
                }

                args.Result = model;
            }
            catch (Exception ex)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Error reading " + fileName);
                var indent = "\t";
                while (ex != null)
                {
                    sb.AppendLine(indent + ex.Message);
                    ex = ex.InnerException;
                    indent += "\t";
                }

                args.Result = new Exception(sb.ToString());
            }
        }

        private void dlg_OpenAnyFile(object sender, CancelEventArgs e)
        {
            var dlg = sender as OpenFileDialog;
            if (dlg != null) LoadAnyModel(dlg.FileName);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="modelFileName"></param>
        public void LoadAnyModel(string modelFileName)
        {
            var fInfo = new FileInfo(modelFileName);
            if (!fInfo.Exists) // file does not exist; do nothing
                return;
            if (fInfo.FullName.ToLower() == GetOpenedModelFileName()) //same file do nothing
                return;

            // there's no going back; if it fails after this point the current file should be closed anyway
            CloseAndDeleteTemporaryFiles();

            SetOpenedModelFileName(modelFileName.ToLower());

            ProgressStatusBar.Visibility = Visibility.Visible;
            CreateWorker();

            var ext = fInfo.Extension.ToLower();
            switch (ext)
            {
                case ".ifc": //it is an Ifc File
                case ".ifcxml": //it is an IfcXml File
                case ".ifczip": //it is a xip file containing xbim or ifc File
                case ".zip": //it is a xip file containing xbim or ifc File
                    _worker.DoWork += OpenIfcFile;
                    _worker.RunWorkerAsync(modelFileName);
                    break;
                case ".xbimf":
                case ".xbim": //it is an xbim File, just open it in the main thread
                    _worker.DoWork += OpenXbimFile;
                    _worker.RunWorkerAsync(modelFileName);
                    break;
            }
            
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="s"></param>
        /// <param name="args"></param>
        public delegate void LoadingCompleteEventHandler(object s, RunWorkerCompletedEventArgs args);
        /// <summary>
        /// 
        /// </summary>
        public event LoadingCompleteEventHandler LoadingComplete;

        private void FireLoadingComplete(object s, RunWorkerCompletedEventArgs args)
        {
            if (LoadingComplete != null)
            {
                LoadingComplete(s, args);
            }
        }

        private void CreateWorker()
        {
            _worker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            _worker.ProgressChanged += delegate(object s, ProgressChangedEventArgs args)
            {
                if (args.ProgressPercentage < 0 || args.ProgressPercentage > 100) 
                    return;
                ProgressBar.Value = args.ProgressPercentage;
                StatusMsg.Text = (string) args.UserState;
            };

            _worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs args)
            {
                if (args.Result is XbimModel) //all ok
                {
                    ModelProvider.ObjectInstance = args.Result; //this Triggers the event to load the model into the views 
                    // PropertiesControl.Model = (XbimModel)args.Result; // // done thtough binding in xaml
                    ModelProvider.Refresh();
                    ProgressBar.Value = 0;
                    StatusMsg.Text = "Ready";

                    AddRecentFile();

                    // todo: file extensions need to be registered to allow an easy use of the jumplist
                    // this is best done in the installer.


                    //// METHOD 1 - for a task
                    //// Configure a new JumpTask.
                    //// 
                    //FileInfo Fi = new FileInfo(_openedModelFileName);
                    //JumpTask jumpTask1 = new JumpTask();
                    //// Get the path to Calculator and set the JumpTask properties.
                    //jumpTask1.ApplicationPath = Assembly.GetExecutingAssembly().Location;
                    //jumpTask1.Arguments = _openedModelFileName;
                    //jumpTask1.IconResourcePath = Assembly.GetExecutingAssembly().Location;
                    //jumpTask1.Title = Fi.Name;
                    //jumpTask1.Description = Fi.FullName;
                    //var v = JumpList.GetJumpList(Application.Current);
                    //v.JumpItems.Add(jumpTask1);
                    //v.Apply();

                    // METHOD 2: recent files.
                    //// Get the JumpList from the application and update it.
                    //var jumpList1 = JumpList.GetJumpList(App.Current);
                    //jumpList1.JumpItems.Add()
                    //JumpList.AddToRecentCategory(_openedModelFileName);
                    //jumpList1.Apply();
                }
                else //we have a problem
                {
                    var errMsg = args.Result as string;
                    if (!string.IsNullOrEmpty(errMsg))
                        MessageBox.Show(this, errMsg, "Error Opening File",
                                        MessageBoxButton.OK, MessageBoxImage.Error,
                                        MessageBoxResult.None, MessageBoxOptions.None);
                    var exception = args.Result as Exception;
                    if (exception != null)
                    {
                        var sb = new StringBuilder();
                        
                        var indent = "";
                        while (exception != null)
                        {
                            sb.AppendFormat("{0}{1}\n", indent, exception.Message);
                            exception = exception.InnerException;
                            indent += "\t";
                        }
                        MessageBox.Show(this, sb.ToString(), "Error Opening Ifc File",
                                        MessageBoxButton.OK, MessageBoxImage.Error,
                                        MessageBoxResult.None, MessageBoxOptions.None);
                    }
                    ProgressBar.Value = 0;
                    StatusMsg.Text = "Error/Ready";
                }
                FireLoadingComplete(s, args);
            };
        }

        private void dlg_FileSaveAs(object sender, CancelEventArgs e)
        {
            var dlg = sender as SaveFileDialog;
            if (dlg != null)
            {
                var fInfo = new FileInfo(dlg.FileName);
                try
                {
                    if (fInfo.Exists)
                    {
                        // the user has been asked to confirm deletion previously
                        fInfo.Delete();
                    }
                    if (Model != null)
                    {
                        Model.SaveAs(dlg.FileName);
                        SetOpenedModelFileName(dlg.FileName);
                        var s = Path.GetExtension(dlg.FileName);
                        if (string.IsNullOrWhiteSpace(s)) 
                            return;
                        var extension = s.ToLowerInvariant();
                        if (extension != "xbim" || string.IsNullOrWhiteSpace(_temporaryXbimFileName)) 
                            return;
                        File.Delete(_temporaryXbimFileName);
                        _temporaryXbimFileName = null;
                    }
                    else throw new Exception("Invalid Model Server");
                }
                catch (Exception except)
                {
                    MessageBox.Show(except.Message, "Error Saving as", MessageBoxButton.OK,
                                    MessageBoxImage.Error);
                }
            }
        }



        private void CommandBinding_SaveAs(object sender, ExecutedRoutedEventArgs e)
        {
            var dlg = new SaveFileDialog();
            if (GetOpenedModelFileName() != null)
            {
                var f = new FileInfo(GetOpenedModelFileName());
                dlg.DefaultExt = f.Extension;
                dlg.InitialDirectory = f.DirectoryName;
                dlg.FileName = f.Name;
            }
            // filter starts with basic xbim formats
            var corefilters = new[] {
                    "xBIM File (*.xBIM)|*.xBIM", 
                    "IfcXml File (*.IfcXml)|*.ifcxml", 
                    "IfcZip File (*.IfcZip)|*.ifczip"
                };
            var fedFilter = new[] { "xBIM Federation file (*.xBIMF)|*.xbimf" };
            var ifcFilter = new[] { "Ifc File (*.ifc)|*.ifc" };
            string filter;

            if (Model.IsFederation)
            {
                dlg.DefaultExt = "xBIMF";
                var filterA = fedFilter.Concat(ifcFilter).Concat(corefilters).ToArray();
                filter = string.Join("|", filterA);
            }
            else
            {
                var filterA = ifcFilter.Concat(corefilters).Concat(fedFilter).ToArray();
                filter = string.Join("|", filterA);
            }

            dlg.Filter = filter;// Filter files by extension 
            dlg.Title = "Save As";
            dlg.AddExtension = true;

            // Show open file dialog box 
            dlg.FileOk += dlg_FileSaveAs;
            dlg.ShowDialog(this);
        }

        private void CommandBinding_Close(object sender, ExecutedRoutedEventArgs e)
        {
            CloseAndDeleteTemporaryFiles();
        }

        private void CommandBinding_Open(object sender, ExecutedRoutedEventArgs e)
        {
            // Filter files by extension 
            var dlg = new OpenFileDialog {Filter = "Xbim Files|*.xbim;*.xbimf;*.ifc;*.ifcxml;*.ifczip"};
            dlg.FileOk += dlg_OpenAnyFile;
            dlg.ShowDialog(this);
        }

        /// <summary>
        /// Tidies up any open files and closes any open models
        /// </summary>
        private void CloseAndDeleteTemporaryFiles()
        {
            try
            {
                if (_worker != null && _worker.IsBusy)
                    _worker.CancelAsync(); //tell it to stop
                var model = ModelProvider.ObjectInstance as XbimModel;
                SetOpenedModelFileName(null);
                if (model != null)
                {
                    model.Dispose();
                    ModelProvider.ObjectInstance = null;
                    ModelProvider.Refresh();
                }
                // reset layer styler only if not set to defaults
                if (
                    DrawingControl.LayerStylerForceVersion1 &&
                    !(DrawingControl.LayerStyler is LayerStylerTypeAndIfcStyle)
                    ||
                    !(DrawingControl.GeomSupport2LayerStyler is SurfaceLayerStyler)
                    )
                {
                    SetDefaultModeStyler(null, null);
                }
            }
            finally
            {
                if (!(_worker != null && _worker.IsBusy && _worker.CancellationPending)) //it is still busy but has been cancelled 
                {
                    if (!string.IsNullOrWhiteSpace(_temporaryXbimFileName) && File.Exists(_temporaryXbimFileName))
                        File.Delete(_temporaryXbimFileName);
                    _temporaryXbimFileName = null;
                } //else do nothing it will be cleared up in the worker thread
            }
        }

        private void CommandBinding_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            if (_worker != null && _worker.IsBusy)
                e.CanExecute = false;
            else
            {
                if (e.Command == ApplicationCommands.Close || e.Command == ApplicationCommands.SaveAs)
                {
                    var model = ModelProvider.ObjectInstance as XbimModel;
                    e.CanExecute = (model != null);
                }
                else if (e.Command == OpenExportWindowCmd)
                {
                    var model = ModelProvider.ObjectInstance as XbimModel;
                    e.CanExecute = (model != null) && (!string.IsNullOrEmpty(GetOpenedModelFileName()));
                }
                else
                    e.CanExecute = true; //for everything else
            }
        }


        #endregion

        # region "Federation Model operations"
        private void EditFederationCmdExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            var fdlg = new FederatedModelDialog {DataContext = Model};
            var done = fdlg.ShowDialog();
            if (done.HasValue && done.Value)
            {
                // todo: is there something that needs to happen here?
            }
        }
        private void EditFederationCmdCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Model != null && Model.IsFederation;
        }
        
        private void OpenFederationCmdExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select an existing federate.",
                Filter = "Xbim Federation Files|*.xbimf", // Filter files by extension 
                CheckFileExists = true,
                Multiselect = false
            };

            var done = dlg.ShowDialog(this);

            if (!done.Value) 
                return;
            
            FederationFromDialogbox(dlg);
        }

        private void CreateFederationCmdExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select model files to federate.",
                Filter = "Model Files|*.ifc;*.ifcxml;*.ifczip", // Filter files by extension 
                CheckFileExists = true,
                Multiselect = true
            };
            

            var done = dlg.ShowDialog(this);

            if (!done.Value)
                return;

            FederationFromDialogbox(dlg);
        }


        private void FederationFromDialogbox(OpenFileDialog dlg)
        {
            if (!dlg.FileNames.Any())
                return;
            //use the first filename it's extension to decide which action should happen
            var s = Path.GetExtension(dlg.FileNames[0]);
            if (s == null)
                return;
            var firstExtension = s.ToLower();

            XbimModel fedModel = null;
            switch (firstExtension)
            {
                case ".xbimf":
                    if (dlg.FileNames.Length > 1)
                    {
                        var res = MessageBox.Show("Multiple files selected, open " + dlg.FileNames[0] + "?",
                            "Cannot open multiple Xbim files",
                            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                        if (res == MessageBoxResult.Cancel)
                            return;
                    }
                    fedModel = new XbimModel();
                    fedModel.Open(dlg.FileNames[0], XbimDBAccess.ReadWrite);
                    break;
                case ".ifc":
                case ".ifczip":
                case ".ifcxml":
                    //create temp file as a placeholder for the temperory xbim file
                    var filePath = Path.GetTempFileName();
                    filePath = Path.ChangeExtension(filePath, "xbimf");
                    fedModel = XbimModel.CreateModel(filePath);
                    fedModel.Initialise("Default Author", "Default Organization");
                    using (var txn = fedModel.BeginTransaction())
                    {
                        fedModel.IfcProject.Name = "Default Project Name";
                        txn.Commit();
                    }

                    var informUser = true;
                    for (var i = 0; i < dlg.FileNames.Length; i++)
                    {
                        var fileName = dlg.FileNames[i];
                        var builder = new XbimReferencedModelViewModel
                        {
                            Name = fileName,
                            OrganisationName = "OrganisationName " + i,
                            OrganisationRole = "Undefined"
                        };

                        var buildRes = false;
                        Exception exception = null;
                        try
                        {
                            buildRes = builder.TryBuild(fedModel);
                        }
                        catch (Exception ex)
                        {
                            //usually an EsentDatabaseSharingViolationException, user needs to close db first
                            exception = ex;
                        }

                        if (buildRes || !informUser)
                            continue;
                        var msg = exception == null ? "" : "\r\nMessage: " + exception.Message;
                        var res = MessageBox.Show(fileName + " couldn't be opened." + msg + "\r\nShow this message again?",
                            "Failed to open a file", MessageBoxButton.YesNoCancel, MessageBoxImage.Error);
                        if (res == MessageBoxResult.No)
                            informUser = false;
                        else if (res == MessageBoxResult.Cancel)
                        {
                            fedModel = null;
                            break;
                        }
                    }
                    break;
            }
            if (fedModel == null)
                return;
            CloseAndDeleteTemporaryFiles();
            ModelProvider.ObjectInstance = fedModel;
            ModelProvider.Refresh();
        }

        private void OpenFederationCmdCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }
        #endregion

        /// <summary>
        /// 
        /// </summary>
        public IPersistIfcEntity SelectedItem
        {
            get { return (IPersistIfcEntity)GetValue(SelectedItemProperty); }
            set { SetValue(SelectedItemProperty, value); }
        }

        // Using a DependencyProperty as the backing store for SelectedItem.  This enables animation, styling, binding, etc...
        /// <summary>
        /// 
        /// </summary>
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register("SelectedItem", typeof(IPersistIfcEntity), typeof(XplorerMainWindow),
                                        new UIPropertyMetadata(null, OnSelectedItemChanged));


        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var mw = d as XplorerMainWindow;
            if (mw != null && e.NewValue is IPersistIfcEntity)
            {
                var label = (IPersistIfcEntity)e.NewValue;
                mw.EntityLabel.Text = label != null ? "#" + label.EntityLabel : "";
            }
            else if (mw != null) mw.EntityLabel.Text = "";
        }


        private ObjectDataProvider ModelProvider
        {
            get
            {
                return MainFrame.DataContext as ObjectDataProvider;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public XbimModel Model
        {
            get
            {
                var op = MainFrame.DataContext as ObjectDataProvider;
                return op == null ? null : op.ObjectInstance as XbimModel;
            }
        }

        // this variable is used to determine when the user is trying again to double click on the selected item
        // from this we detect that he's probably not happy with the view, therefore we add a cutting plane to make the 
        // element visible.
        //
        private bool _camChanged;
        
        private void SpatialControl_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            _camChanged = false;
            DrawingControl.Viewport.Camera.Changed += Camera_Changed;
            DrawingControl.ZoomSelected();
            DrawingControl.Viewport.Camera.Changed -= Camera_Changed;
            if (!_camChanged)
                DrawingControl.ClipBaseSelected(0.15);
        }

        void Camera_Changed(object sender, EventArgs e)
        {
            _camChanged = true;
        }


        private void MenuItem_ZoomExtents(object sender, RoutedEventArgs e)
        {
            DrawingControl.ViewHome();
        }
        
        private void OpenExportWindow(object sender, ExecutedRoutedEventArgs executedRoutedEventArgs)
        {
            var wndw = new ExportWindow(this);
            wndw.ShowDialog();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var w = new AboutWindow {Model = Model};
            w.Show();
        }
        
        private void DisplaySettingsPage(object sender, RoutedEventArgs e)
        {
            var sett = new SettingsWindow();
            sett.ShowDialog();
            if (sett.SettingsChanged)
                InitFromSettings();
        }

        private void RecentFileClick(object sender, RoutedEventArgs e)
        {
            var obMenuItem = e.OriginalSource as MenuItem;
            if (obMenuItem != null)
            {
                var fileName = obMenuItem.Header.ToString();
                if (!File.Exists(fileName))
                {
                    return;
                }
                LoadAnyModel(fileName);
            }
        }

        private void SetDefaultModeStyler(object sender, RoutedEventArgs e)
        {
            DrawingControl.LayerStyler = new LayerStylerTypeAndIfcStyle();
            DrawingControl.GeomSupport2LayerStyler = new SurfaceLayerStyler();
            DrawingControl.LayerStylerForceVersion1 = false;
            DrawingControl.ReloadModel();
        }

        private void SetStylerVersion1(object sender, RoutedEventArgs e)
        {
            DrawingControl.LayerStyler = new LayerStylerTypeAndIfcStyle();
            DrawingControl.LayerStylerForceVersion1 = true;
            DrawingControl.ReloadModel();
        }

        private void SetFederationStylerRole(object sender, RoutedEventArgs e)
        {
            DrawingControl.FederationLayerStyler = new LayerStylerSingleColour();
            DrawingControl.LayerStylerForceVersion1 = true;
            DrawingControl.ReloadModel();
        }

        private void SetFederationStylerType(object sender, RoutedEventArgs e)
        {
            DrawingControl.FederationLayerStyler = new LayerStylerTypeAndIfcStyle();
            DrawingControl.LayerStylerForceVersion1 = true;
            DrawingControl.ReloadModel();
        }

        DrawingControl3D IXbimXplorerPluginMasterWindow.DrawingControl
        {
            get { return DrawingControl; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OpenWindow(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            if (mi == null)
                return;
            OpenOrFocusPluginWindow(mi.Tag as Type);
        }

        private void ShowErrors(object sender, MouseButtonEventArgs e)
        {
            OpenOrFocusPluginWindow(typeof (LogViewer.LogViewer));
        }

        private void Exit(object sender, RoutedEventArgs e)
        {
            using (var fs = new StringWriter())
            {
                var xmlLayout = new XmlLayoutSerializer(DockingManager);
                xmlLayout.Serialize(fs);
                var xmlLayoutString = fs.ToString();
                Clipboard.SetText(xmlLayoutString);
            }
            Close();
        }

        private void RenderedEvents(object sender, EventArgs e)
        {
            // command line arg can prevent plugin loading
            if (Settings.Default.PluginStartupLoad && !_blockPlugin)
                RefreshPlugins();
        }
    }
}
