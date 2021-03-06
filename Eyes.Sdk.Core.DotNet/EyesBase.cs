﻿using Applitools.Utils;
using Applitools.Utils.Geometry;
using Applitools.Utils.Images;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using Region = Applitools.Utils.Geometry.Region;
using System.Diagnostics;
using Applitools.Fluent;
using Applitools.Exceptions;
using Applitools.Utils.Cropping;
using Applitools.Cropping;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Applitools.VisualGrid.Model;
using Applitools.VisualGrid;

namespace Applitools
{

    /// <summary>
    /// Extracts text in the specified language from the input image region.
    /// </summary>
    public delegate string[] GetTextHandler(string imageId, IList<Rectangle> regions, string lanuage);

    /// <summary>
    /// Creates an image of the specified region of the application window and returns its id.
    /// </summary>
    /// <param name="region">Image region or an empty rectangle to create an image of 
    /// the entire window.</param>
    public delegate string CreateImageHandler(Rectangle region);

    /// <summary>
    /// Applitools Eyes base class.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Microsoft.Design",
        "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
        Justification = "Cleanup performed by Close or AbortIfNotClosed")]
    [ComVisible(true)]
    public abstract class EyesBase : EyesBaseConfig, IEyesBase, IBatchCloser
    {

        #region Fields

        protected internal RunningSession runningSession_;
        protected MatchWindowTask matchWindowTask_;
        private SessionStartInfo sessionStartInfo_;
        private bool shouldMatchWindowRunOnceOnTimeout_;
        private IScaleProvider scaleProvider_;
        private SetScaleProviderHandler setScaleProvider_;
        private ICutProvider cutProvider_;
        private Assembly actualAssembly_;
        private PropertiesCollection properties_;
        private static readonly object screenshotLock_ = new object();
        private bool isViewportSizeSet_;
        private IDebugScreenshotProvider debugScreenshotProvider_ = NullDebugScreenshotProvider.Instance;
        protected RectangleSize viewportSize_;
        public static string DefaultServerUrl = "https://eyesapi.applitools.com/";
        private IServerConnector serverConnector_;
        private Dictionary<IosDeviceName, DeviceSize> iosDevicesSizes_;
        private Dictionary<DeviceName, DeviceSize> emulatedDevicesSizes_;
        private Dictionary<BrowserType, string> userAgents_;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new <see cref="EyesBase"/> instance that interacts with 
        /// the Eyes Server at the specified url.
        /// </summary>
        /// <param name="serverUrl">The Eyes server URL.</param>
        protected EyesBase(Uri serverUrl) : this()
        {
            ArgumentGuard.NotNull(serverUrl, nameof(serverUrl));
            ServerUrl = serverUrl.ToString();
        }

        /// <summary>
        /// Creates a new <see cref="EyesBase"/> instance that interacts with the Eyes cloud
        /// service.
        /// </summary>
        protected EyesBase() : this(new ServerConnectorFactory()) { }

        protected EyesBase(IServerConnectorFactory serverConnectorFactory)
        {
            ServerConnectorFactory = serverConnectorFactory;

            Logger = new Logger();

            //EnsureConfiguration_();

            ServerConnector = ServerConnectorFactory.CreateNewServerConnector(Logger);
            runningSession_ = null;
            UserInputs = new List<Trigger>();

            properties_ = new PropertiesCollection();

            setScaleProvider_ = provider => { scaleProvider_ = provider; };
            scaleProvider_ = NullScaleProvider.Instance;
            cutProvider_ = NullCutProvider.Instance;

            StackTrace stackTrace = new StackTrace();
            StackFrame[] stackFrames = stackTrace.GetFrames();
            foreach (StackFrame stackFrame in stackFrames)
            {
                Type callingType = stackFrame.GetMethod().DeclaringType;
                if (callingType.IsAbstract) continue;
                actualAssembly_ = callingType.Assembly;
                break;
            }
        }

        protected abstract Configuration Configuration { get; }

        #endregion

        #region Properties
        internal protected override Configuration Config { get => Configuration; }

        /// <summary>
        /// Whether or not the Eyes api is disabled. 
        /// If <c>true</c>, all interactions with this API are silently ignored.
        /// </summary>
        public virtual bool IsDisabled { get; set; }

        /// <summary>
        /// User inputs collected between <see cref="CheckWindowBase(Rectangle?, ICheckSettings, string)"/> (or one of its overloads) invocations.
        /// </summary>
        public IList<Trigger> UserInputs { get; private set; }

        /// <summary>
        /// Gets or sets <see cref="MatchLevel"/> by its numeric value (to simplify interop)
        /// </summary>
        public int InteropMatchLevel
        {
            get { return (int)DefaultMatchSettings.MatchLevel; }
            set { DefaultMatchSettings.MatchLevel = (MatchLevel)value; }
        }

        [Obsolete("Use " + nameof(BaselineBranchName) + " instead.")]
        public bool? CompareWithParentBranch { get; set; }

        /// <summary>
        /// Gets or sets <see cref="FailureReports"/> by its numeric value (to simplify interop)
        /// </summary>
        public int InteropFailureReports
        {
#pragma warning disable CS0612 // Type or member is obsolete
            get { return (int)FailureReports; }
            set { FailureReports = (FailureReports)value; }
#pragma warning restore CS0612 // Type or member is obsolete
        }

        /// <summary>
        /// Gets or sets <see cref="Configuration.MatchTimeout"/> in milliseconds (to simplify interop)
        /// </summary>
        public int InteropMatchTimeout
        {
            get { return (int)Configuration.MatchTimeout.TotalMilliseconds; }
            set { Configuration.SetMatchTimeout(TimeSpan.FromMilliseconds(value)); }
        }

        /// <summary>
        /// Message logger.
        /// </summary>
        public Logger Logger { get; private set; }

        /// <summary>
        /// The agent id.
        /// </summary>
        protected virtual string BaseAgentId => GetBaseAgentId();

        internal string GetBaseAgentId()
        {
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(actualAssembly_.Location);
            return $"{versionInfo.FileDescription}/{versionInfo.ProductVersion}";
        }

        /// <summary>
        /// Gets the full agent id including both <see cref="EyesBaseConfig.AgentId"/> and 
        /// <see cref="BaseAgentId"/>.
        /// </summary>
        public string FullAgentId
        {
            get
            {
                string agentId = Configuration?.AgentId;
                return (agentId == null) ? BaseAgentId : $"{agentId} [{BaseAgentId}]";
            }
        }

        /// <summary>
        /// Gets or sets the proxy used to access the Eyes server or <c>null</c> to use the system 
        /// proxy.
        /// </summary>
        public WebProxy Proxy
        {
            get { return ServerConnector.Proxy; }
            set { ServerConnector.Proxy = value; }
        }

        protected SetScaleProviderHandler SetScaleProvider
        {
            get { return setScaleProvider_; }
        }

        /// <summary>
        /// Sets the current scale provider (assuming <see cref="setScaleProvider_"/> is not 
        /// in read-only mode).
        /// </summary>
        public IScaleProvider ScaleProvider
        {
            get { return scaleProvider_; }
            set { setScaleProvider_(value); }
        }

        /// <summary>
        /// Get/Set the ratio to use for scaling images before validating them. Setting the value 
        /// to 0 (or lower) will cause the Eyes SDK to use the default values.
        /// </summary>
        public double ScaleRatio
        {
            get { return scaleProvider_.ScaleRatio; }
            set
            {
                if (value > 0)
                {
                    // Scale ratio will no longer be automatically determined (as it was set by the user).
                    setScaleProvider_ = provider => { return; };
                    scaleProvider_ = new FixedScaleProvider(value);
                }
                else // Switch back to automatically identifying scale ratio.
                {
                    setScaleProvider_ = provider => { scaleProvider_ = provider; };
                    scaleProvider_ = new FixedScaleProvider(1);
                }
            }
        }

        public IPositionProvider PositionProvider { get; protected set; }

        public IDebugScreenshotProvider DebugScreenshotProvider
        {
            get { return debugScreenshotProvider_; }
            set
            {
                ArgumentGuard.NotNull(value, nameof(DebugScreenshotProvider));
                debugScreenshotProvider_ = value;
                debugScreenshotProvider_.SetLogger(Logger);
            }
        }

        public ICutProvider CutProvider
        {
            get => cutProvider_;
            set => cutProvider_ = value ?? NullCutProvider.Instance;
        }

        public bool IsCutProviderExplicitlySet
        {
            get { return CutProvider != null && !(CutProvider is NullCutProvider); }
        }

        #endregion

        #region Methods

        #region Public

        /// <summary>
        /// Sets a handler of log messages generated by this API.
        /// </summary>
        /// <param name="logHandler">Handles log messages generated by this API.</param>
        public void SetLogHandler(ILogHandler logHandler)
        {
            Logger.SetLogHandler(logHandler);
        }

        /// <summary>
        /// Ends the test and returns its results.
        /// </summary>
        public virtual TestResults Close()
        {
            return Close(true);
        }

        /// <summary>
        /// Ends the test.
        /// </summary>
        /// <param name="throwEx">
        /// Whether to throw an exception if the test is new or mismatches were found</param>
        /// <exception cref="TestFailedException">
        /// Thrown if mismatches were found.</exception>
        /// <exception cref="NewTestException">A new test ended</exception>
        /// <returns>The test results.</returns>
        public virtual TestResults Close(bool throwEx)
        {
            try
            {
                if (IsDisabled)
                {
                    Logger.Verbose("Ignored");
                    return new TestResults() { ServerConnector = ServerConnector };
                }

                Logger.Verbose("enter (hashcode: {0})", GetHashCode());

                ArgumentGuard.IsValidState(IsOpen, "Eyes not open");

                IsOpen = false;

                if (matchWindowTask_ != null)
                {
                    matchWindowTask_.Dispose();
                    matchWindowTask_ = null;
                }

                try
                {
                    CloseOrAbort(false);
                }
                catch (Exception ex)
                {
                    Logger.Log("Close failed: {0}", ex.Message);
                }

                if (runningSession_ == null)
                {
                    Logger.Verbose("Server session was not started");
                    Logger.Log("--- Empty test ended.");
                    return new TestResults() { ServerConnector = ServerConnector };
                }

                bool isNewSession = runningSession_.IsNewSession;

                Logger.Verbose("Ending server session...");
                var save = (isNewSession && Configuration.SaveNewTests) || (!isNewSession && (Configuration.SaveDiffs ?? false));

                TestResults results = ServerConnector.EndSession(runningSession_, false, save);
                results.IsNew = isNewSession;
                results.Url = runningSession_.Url;

                Logger.Verbose(results.ToString());

                LogSessionResultsAndThrowException(Logger, throwEx, results);

                results.ServerConnector = ServerConnector;
                return results;
            }
            finally
            {
                // Making sure that we reset the running session even if an
                // exception was thrown during close.
                viewportSize_ = null;
                runningSession_ = null;
                Logger.GetILogHandler().Close();
            }
        }

        public static void LogSessionResultsAndThrowException(Logger logger, bool throwEx, TestResults results)
        {
            if (results.Status == TestResultsStatus.Unresolved)
            {
                if (results.IsNew)
                {
                    string instructions = "Please approve the new baseline at " + results.Url;
                    logger.Log("--- New test ended. " + instructions);

                    if (throwEx)
                    {
                        string message = "'" + results.Name
                                + "' of '" + results.AppName
                                + "'. " + instructions;
                        throw new NewTestException(results, message);
                    }
                }
                else
                {
                    logger.Log("--- Differences found. See details at " + results.Url);

                    if (throwEx)
                    {
                        throw new DiffsFoundException(results, results.Name, results.AppName, results.Url);
                    }
                }
            }
            else if (results.Status == TestResultsStatus.Failed)
            {
                logger.Log("--- Failed test ended. See details at " + results.Url);

                if (throwEx)
                {
                    string message = "'" + results.Name
                             + "' of '" + results.AppName
                             + "'. See details at " + results.Url;

                    throw new TestFailedException(results, message);
                }
            }
            else
            {
                // Test passed
                logger.Log("--- Test passed. See details at " + results.Url);
            }
        }

        /// <summary>
        /// If a test is running, aborts it. Otherwise, does nothing.
        /// </summary>
        public TestResults AbortIfNotClosed()
        {
            return Abort();
        }

        /// <summary>
        /// If a test is running, aborts it. Otherwise, does nothing.
        /// </summary>
        public TestResults Abort()
        {
            try
            {
                if (IsDisabled)
                {
                    Logger.Verbose("Ignored");
                    return null;
                }

                Logger.Verbose("enter (hashcode: {0})", GetHashCode());

                IsOpen = false;

                if (matchWindowTask_ != null)
                {
                    matchWindowTask_.Dispose();
                    matchWindowTask_ = null;
                }

                if (null == runningSession_)
                {
                    return null;
                }

                try
                {
                    CloseOrAbort(true);
                }
                catch (Exception ex)
                {
                    Logger.Log("Close failed: {0}", ex.Message);
                }

                Logger.Verbose("Aborting server session...");
                try
                {
                    TestResults results = ServerConnector.EndSession(runningSession_, true, false);
                    results.IsNew = runningSession_.IsNewSession;
                    results.Url = runningSession_.Url;
                    Logger.Log("Test aborted");
                    return results;
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed to abort server session: " + ex.Message);
                }
            }
            finally
            {
                runningSession_ = null;
                Logger.GetILogHandler().Close();
            }
            return null;
        }

        public void AbortAsync()
        {
            Abort();
        }

        /// <summary>
        /// Sets the OS (e.g., Windows) and application (e.g., Chrome) 
        /// that host the application under test.
        /// </summary>
        /// <param name="hostOS">The name of the OS hosting the application under test or 
        /// <c>null</c> to auto-detect.</param>
        /// <param name="hostApp">The name of the application hosting the application under
        /// test or <c>null</c> to auto-detect.</param>
        public void SetAppEnvironment(string hostOS, string hostApp)
        {
            if (IsDisabled)
            {
                Logger.Verbose("Ignored");
                return;
            }

            Logger.Verbose("SetAppEnvironment({0}, {1})", hostOS, hostApp);

            if (string.IsNullOrWhiteSpace(hostOS))
            {
                hostOS = null;
            }
            else
            {
                hostOS = hostOS.Trim();
            }

            if (string.IsNullOrWhiteSpace(hostApp))
            {
                hostApp = null;
            }
            else
            {
                hostApp = hostApp.Trim();
            }

            Configuration.SetHostOS(hostOS);
            Configuration.SetHostApp(hostApp);
        }

        public void AddProperty(string name, string value)
        {
            properties_.Add(name, value);
        }

        public void ClearProperties()
        {
            properties_.Clear();
        }

        /// <summary>
        /// Writes the input message to this agent's log.
        /// </summary>
        public void Log(string message)
        {
            ArgumentGuard.NotNull(message, nameof(message));

            Logger.Log(message);
        }

        /// <summary>
        /// Throws an <see cref="EyesException"/> with the input message.
        /// </summary>
        public void Throw(string message)
        {
            Logger.Log(message);
            throw new EyesException(message);
        }

        #endregion

        #region Protected

        /// <summary>
        /// Specifies a region of the current application window.
        /// </summary>
        /// <param name="getRegion">Gets the region bounds (invoked after viewport size is set)
        /// </param>
        protected InRegionBase InRegionBase(Func<Rectangle> getRegion)
        {
            ArgumentGuard.NotNull(getRegion, nameof(getRegion));

            if (runningSession_ == null)
            {
                StartSession_();
            }

            Rectangle region = getRegion();

            string[] getText(string imageId, IList<Rectangle> regions, string language)
            {
                string traceMsg = Tracer.FormatCall("GetText", imageId).WithArg(regions).WithArg(language);

                try
                {
                    string[] text = ServerConnector.GetTextInRunningSessionImage(runningSession_, imageId, regions, language);
                    Logger.Verbose("{0} => {1}", traceMsg, string.Join(", ", text));
                    return text;
                }
                catch (Exception ex)
                {
                    Logger.Log("{0} failed: {1}", traceMsg, Tracer.FormatException(ex));
                    throw;
                }
            };

            return new InRegionBase(null, region, CreateImage_, getText);
        }

        private string CreateImage_(Rectangle bounds)
        {
            EyesScreenshot screenshot = null;
            string traceMsg = Tracer.FormatCall("CreateImage", GeometryUtils.ToString(bounds));

            // We take only one screenshot per call to InRegionBase.
            lock (screenshotLock_)
            {
                if (screenshot == null)
                {
                    screenshot = GetScreenshot_();
                }
            }

            try
            {
                using (Bitmap regionImage = screenshot.Image.Clone(bounds, screenshot.Image.PixelFormat))
                {
                    DebugScreenshotProvider.Save(regionImage, $"_{bounds.X}_{bounds.Y}_{bounds.Width}_{bounds.Height}");

                    string imageId = ServerConnector.AddRunningSessionImage(
                        runningSession_, regionImage.GetStream().ToArray());

                    Logger.Verbose("{0} => {1}", traceMsg, imageId);

                    return imageId;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("{0} failed: {1}", traceMsg, Tracer.FormatException(ex));
                throw;
            }
        }

        /// <summary>
        /// Takes a snapshot of the application under test and matches it with the expected output.
        /// </summary>
        /// <param name="region">The rectangle to be captured in the screenshot. Pass <c>Rectangle.Empty</c> or <c>null</c> for the entire screenshot.</param>
        /// <param name="tag">An optional tag to be associated with the snapshot.</param>
        /// <param name="retryTimeout">The amount of time to retry matching in milliseconds or a negative value to use the default retry timeout.</param>
        /// <param name="source">The tested source (URL or App Name).</param>
        /// <exception cref="TestFailedException">
        /// Thrown if a mismatch is detected and immediate failure reports are enabled.
        /// </exception>
        protected MatchResult CheckWindowBase(Rectangle? region, string tag, int retryTimeout = -1, string source = null)
        {
            CheckSettings checkSettings = new CheckSettings();
            return CheckWindowBase(region, (ICheckSettingsInternal)checkSettings
                .WithName(tag).Timeout(TimeSpan.FromMilliseconds(retryTimeout)), source);
        }

        /// <summary>
        /// Takes a snapshot of the application under test and matches it with the expected output.
        /// </summary>
        /// <param name="region">The rectangle to be captured in the screenshot. Pass <c>Rectangle.Empty</c> or <c>null</c>  for the entire screenshot.</param>
        /// <param name="checkSettings">The settings to use.</param>
        /// <param name="source">The tested source (URL or App Name).</param>
        /// 
        /// <returns></returns>
        protected MatchResult CheckWindowBase(Rectangle? region, ICheckSettings checkSettings, string source = null)
        {
            return CheckWindowBase(region, (ICheckSettingsInternal)checkSettings, source);
        }

        /// <summary>
        /// Takes a snapshot of the application under test and matches it with the expected output.
        /// </summary>
        /// <param name="region">The rectangle to be captured in the screenshot. Pass <c>Rectangle.Empty</c> or <c>null</c>  for the entire screenshot.</param>
        /// <param name="checkSettingsInternal">The settings to use.</param>
        /// <param name="source">The tested source (URL or App Name).</param>
        /// 
        /// <exception cref="TestFailedException">
        /// Thrown if a mismatch is detected and immediate failure reports are enabled.
        /// </exception>
        protected MatchResult CheckWindowBase(Rectangle? region, ICheckSettingsInternal checkSettingsInternal,
            string source = null)
        {
            try
            {
                if (IsDisabled)
                {
                    Logger.Verbose("Ignored");
                    return new MatchResult() { AsExpected = true };
                }
                
                string tag = checkSettingsInternal.GetName() ?? string.Empty;

                ArgumentGuard.IsValidState(IsOpen, "Eyes not open");

                EnsureRunningSession();

                BeforeMatchWindow();

                MatchResult result = MatchWindow_(region, tag, checkSettingsInternal, source);

                AfterMatchWindow();

                if (!checkSettingsInternal.GetReplaceLast())
                {
                    UserInputs.Clear();
                }

                ValidateResult_(tag, result);

                return result;
            }
            catch (Exception ex)
            {
                Logger.Log("Exception: {0}", Tracer.FormatException(ex));
                throw;
            }
        }

        protected virtual string TryCaptureDom()
        {
            return null;
        }

        protected virtual void BeforeMatchWindow() { }
        protected virtual void AfterMatchWindow() { }


        protected virtual void BeforeOpen() { }
        protected virtual void AfterOpen() { }

        private MatchResult MatchWindow_(Rectangle? region, string tag, ICheckSettingsInternal checkSettingsInternal,
            string source)
        {
            Logger.Verbose("enter");

            MatchResult result = matchWindowTask_.MatchWindow(region,
                UserInputs, tag, shouldMatchWindowRunOnceOnTimeout_, checkSettingsInternal.GetReplaceLast(), checkSettingsInternal,
                source);

            return result;
        }

        private string TryPostDomCapture_(string domJson)
        {
            if (domJson == null) return null;
            return ServerConnector.PostDomCapture(domJson);
        }

        private void ValidateResult_(string tag, MatchResult result)
        {
            if (result.AsExpected)
            {
                return;
            }

            shouldMatchWindowRunOnceOnTimeout_ = true;

            if (!runningSession_.IsNewSession)
            {
                Logger.Log("Mismatch!{0}", tag == null ? string.Empty : " (" + tag + ")");
            }

#pragma warning disable CS0612 // Type or member is obsolete
            if (FailureReports == FailureReports.Immediate)
#pragma warning restore CS0612 // Type or member is obsolete
            {
                throw new TestFailedException("Mismatch found in '" +
                    sessionStartInfo_.ScenarioIdOrName + "' of '" +
                    sessionStartInfo_.AppIdOrName + "'");
            }
        }

        /// <summary>
        /// Starts a test.
        /// </summary>
        /// <param name="appName">The name of the application under test.</param>
        /// <param name="testName">The test name.</param>
        /// <param name="viewportSize">The required application's client area viewport size
        /// or <c>Size.Empty</c> to allow any viewport size.</param>
        protected void OpenBase(string appName, string testName, Size viewportSize)
        {
            RectangleSize size = null;
            if (!viewportSize.IsEmpty)
            {
                size = new RectangleSize(viewportSize);
            }

            OpenBase(appName, testName, size);
        }

        /// <summary>
        /// Starts a test.
        /// </summary>
        /// <param name="appName">The name of the application under test.</param>
        /// <param name="testName">The test name.</param>
        /// <param name="viewportSize">The required application's client area viewport size
        /// or <c>null</c> to allow any viewport size.</param>
        protected void OpenBase(string appName, string testName, RectangleSize viewportSize)
        {
            ArgumentGuard.NotNull(testName, nameof(testName));
            Configuration.SetTestName(testName);
            Configuration.SetAppName(appName ?? Configuration.AppName);
            Configuration.SetViewportSize(viewportSize);

            Logger.Verbose("Open({0}, {1}, {2})", appName, testName, viewportSize);

            OpenBase();
        }

        /// <summary>
        /// Starts a test using the current <c>Configuration</c> data.
        /// </summary>
        protected void OpenBase()
        {
            Logger.GetILogHandler().Open();

            try
            {
                if (IsDisabled)
                {
                    Logger.Verbose("Ignored");
                    return;
                }

                Logger.Log("Agent = {0}", FullAgentId);
                Logger.Verbose(".NET Framework = {0}", Environment.Version);

                ValidateAPIKey(ApiKey);
                UpdateServerConnector_();
                LogOpenBase_();
                ValidateSessionOpen_();

                InitProviders_();

                isViewportSizeSet_ = false;

                BeforeOpen();

                viewportSize_ = GetViewportSizeForOpen();
                EnsureRunningSession();

                IsOpen = true;
                Logger.Verbose("EyesBase now open. (hashcode: {0})", GetHashCode());

                AfterOpen();
            }
            catch (Exception ex)
            {
                Logger.Log("Open(): {0}", Tracer.FormatException(ex));
                Logger.GetILogHandler().Close();
                throw;
            }
        }

        internal void UpdateServerConnector_()
        {
            ServerConnector.ApiKey = ApiKey;
            ServerConnector.ServerUrl = new Uri(ServerUrl);
        }

        protected virtual RectangleSize GetViewportSizeForOpen()
        {
            return Configuration.ViewportSize;
        }

        private void InitProviders_()
        {
            ScaleProvider = new NullScaleProvider();
            if (CutProvider == null)
            {
                CutProvider = new NullCutProvider();
            }
            PositionProvider = null;
        }

        public static void ValidateAPIKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                var errMsg = "API key not set! Log in to https://applitools.com to obtain your API key and use the 'Eyes.ApiKey' property to set it.";
                throw new EyesException(errMsg);
            }
        }

        private void ValidateSessionOpen_()
        {
            if (IsOpen)
            {
                AbortIfNotClosed();
                string errMsg = "A test is already running";
                Logger.Log(errMsg);
                throw new EyesException(errMsg);
            }
        }

        private void LogOpenBase_()
        {
            Logger.Log("Eyes Server URL is {0}", ServerConnector.ServerUrl);
            Logger.Verbose("Timeout = {0}", ServerConnector.Timeout);
            Logger.Log("MatchTimeout = {0}", Configuration.MatchTimeout);
            Logger.Log("DefaultMatchSettings = {0}", DefaultMatchSettings);
        }

        protected virtual void EnsureRunningSession()
        {
            if (runningSession_ != null)
            {
                Logger.Log("Session already running.");
                return;
            }

            Logger.Log("No running session, calling start session...");
            StartSession_();
            Logger.Log("Done!");

            matchWindowTask_ = new MatchWindowTask(
                Logger,
                ServerConnector,
                runningSession_,
                Configuration.MatchTimeout,
                this,
                // A callback which will call getAppOutput
                GetAppOutput_
            );
        }

        protected abstract Size GetViewportSize();

        protected abstract void SetViewportSize(RectangleSize size);
        protected virtual void SetEffectiveViewportSize(RectangleSize size) { }

        protected abstract string GetInferredEnvironment();

        /// <summary>
        /// Returns a screenshot to validate or <c>null</c> to indicate that a screenshot URL is to be provided.
        /// </summary>
        /// <param name="region">The rectangle to be captured in the screenshot. Pass <c>Rectangle.Empty</c> or <c>null</c> for the entire screenshot.</param>
        /// <param name="checkSettingsInternal"></param>
        protected abstract EyesScreenshot GetScreenshot(Rectangle? region, ICheckSettingsInternal checkSettingsInternal);

        protected virtual EyesScreenshot GetScreenshot(Rectangle? region, ICheckSettingsInternal checkSettingsInternal, ImageMatchSettings imageMatchSettings)
        {
            object data = BeforeGetScreenshot();
            EyesScreenshot result = GetScreenshot(region, checkSettingsInternal);
            AfterGetScreenshot(data);
            return result;
        }

        protected virtual object BeforeGetScreenshot() { return null; }

        protected virtual void AfterGetScreenshot(object data) { }

        protected abstract string GetTitle();

        /// <summary>
        /// Returns a URL from which the application screenshot can be obtained. 
        /// Invoked only if a previous call to <see cref="GetScreenshot(Rectangle?, ICheckSettingsInternal, ImageMatchSettings)"/> returned <c>null</c>.
        /// </summary>
        protected virtual string GetScreenshotUrl()
        {
            return null;
        }

        protected virtual void CloseOrAbort(bool aborted)
        {
        }

        void IBatchCloser.CloseBatch(string batchId)
        {
            if (IsDisabled)
            {
                Logger.Verbose("Ignored");
                return;
            }

            ServerConnector.CloseBatch(batchId);
        }

        protected ILastScreenshotBounds LastScreenshotBoundsProvider { get { return matchWindowTask_; } }

        protected internal IServerConnector ServerConnector
        {
            get
            {
                if (serverConnector_ != null && serverConnector_.AgentId == null)
                {
                    serverConnector_.AgentId = FullAgentId;
                }
                return serverConnector_;
            }

            set => serverConnector_ = value;
        }

        public bool IsOpen { get; private set; }
        protected virtual bool ViewportSizeRequired => true;

        public IServerConnectorFactory ServerConnectorFactory { get; protected internal set; } = new ServerConnectorFactory();
        #endregion

        #region Private

        private EyesScreenshot GetScreenshot_()
        {
            Stopwatch sw = Stopwatch.StartNew();
            EyesScreenshot screenshot = GetScreenshot(null, null);

            Logger.Verbose("GetScreenshot_(): Completed in {0}", sw.Elapsed);

            return screenshot;
        }

        protected internal virtual AppEnvironment GetEnvironment_()
        {
            AppEnvironment appEnv = new AppEnvironment();

            if (Configuration.HostOS != null)
            {
                appEnv.OS = Configuration.HostOS;
            }

            if (Configuration.HostApp != null)
            {
                appEnv.HostingApp = Configuration.HostApp;
            }

            appEnv.Inferred = GetInferredEnvironment();
            if (Configuration.ViewportSize == null && ViewportSizeRequired)
            {
                throw new EyesException("DisplaySize cannot be null. This shouldn't have happened. Please contact support.");
            }
            appEnv.DisplaySize = viewportSize_;
            return appEnv;
        }

        private void StartSession_()
        {
            Logger.Verbose("enter");

            EnsureViewportSize_();

            BatchInfo testBatch;
            if (Configuration.Batch == null)
            {
                Logger.Verbose("No batch set");
                testBatch = new BatchInfo(null);
            }
            else
            {
                Logger.Verbose("Batch is {0}", Configuration.Batch);
                testBatch = Configuration.Batch;
            }

            AppEnvironment appEnv = GetEnvironment_();
            Logger.Verbose("Application environment is {0}", appEnv);

            sessionStartInfo_ = new SessionStartInfo(
                FullAgentId,
                AppName,
                null,
                TestName,
                testBatch,
                Configuration.BaselineEnvName,
                appEnv,
                Configuration.EnvironmentName,
                DefaultMatchSettings,
                Configuration.BranchName,
                Configuration.ParentBranchName,
                Configuration.BaselineBranchName,
                Configuration.SaveDiffs,
                null,
                properties_);

            Logger.Verbose("Starting server session...");
            Logger.Verbose(JsonConvert.SerializeObject(sessionStartInfo_));
            runningSession_ = ServerConnector.StartSession(sessionStartInfo_);

            //Logger.Verbose("getting rendering info...");
            //runningSession_.RenderingInfo = serverConnector_.GetRenderingInfo();

            Logger.Verbose("Server session ID is " + runningSession_.Id);

            string testInfo = $"'{Configuration.TestName}' of '{Configuration.AppName}' {appEnv}";
            if (runningSession_.IsNewSession)
            {
                Logger.Log("--- New test started - " + testInfo);
                shouldMatchWindowRunOnceOnTimeout_ = true;
            }
            else
            {
                Logger.Log("--- Test started - " + testInfo);
                shouldMatchWindowRunOnceOnTimeout_ = false;
            }
        }

        protected virtual object GetAgentSetup() { return null; }

        internal string GetAgentSetupString()
        {
            object agentSetup = GetAgentSetup();
            JsonSerializerSettings serializerSettings = new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Formatting = Formatting.Indented,
                Converters = new[] { new StringEnumConverter() }
            };
            return JsonConvert.SerializeObject(agentSetup, serializerSettings);
        }

        private void EnsureViewportSize_()
        {
            if (!isViewportSizeSet_)
            {
                Logger.Verbose("viewportSize_: {0} ({1})", viewportSize_, GetHashCode());
                if (viewportSize_ == null || viewportSize_.IsEmpty())
                {
                    try
                    {
                        viewportSize_ = GetViewportSize();
                        Logger.Verbose("viewport size: {0} ({1})", viewportSize_, GetHashCode());
                        SetEffectiveViewportSize(viewportSize_);
                    }
                    catch (EyesException)
                    {
                        isViewportSizeSet_ = false;
                    }
                }
                else
                {
                    try
                    {
                        Logger.Verbose("Setting viewport size to {0} ({1})", viewportSize_, GetHashCode());
                        SetViewportSize(viewportSize_);
                        isViewportSizeSet_ = true;
                    }
                    catch (EyesSetViewportSizeException ex)
                    {
                        SetEffectiveViewportSize(ex.ActualViewportSize);
                        isViewportSizeSet_ = false;
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Returns the current application output.
        /// </summary>
        /// <param name="region">The region of the screenshot which will be set in the application output.</param>
        /// <param name="checkSettingsInternal">The check settings object of the current test.</param>
        /// <param name="imageMatchSettings">The image match settings object in which to collect the coded-regions.</param>
        private AppOutputWithScreenshot GetAppOutput_(Rectangle? region, ICheckSettingsInternal checkSettingsInternal, ImageMatchSettings imageMatchSettings)
        {
            string url = null;
            byte[] imageBytes = null;
            EyesScreenshot screenshot = GetScreenshot(region, checkSettingsInternal, imageMatchSettings);
            if (screenshot != null)
            {
                DebugScreenshotProvider.Save(screenshot.Image, "base");
                imageBytes = BasicImageUtils.EncodeAsPng(screenshot.Image);
            }

            MatchWindowTask.CollectRegions(this, screenshot, checkSettingsInternal, imageMatchSettings);

            if (imageBytes == null)
            {
                url = GetScreenshotUrl();
                if (url == null)
                {
                    throw new NullReferenceException("Screenshot URL is null");
                }
                Logger.Verbose("Screenshot URL is {0}", url);
            }

            string title = GetTitle();

            return new AppOutputWithScreenshot(new AppOutput(title, null, imageBytes, url, screenshot?.DomUrl), screenshot);
        }

        protected string TryCaptureAndPostDom(ICheckSettingsInternal checkSettingsInternal)
        {
            string domUrl = null;
            if (checkSettingsInternal.GetSendDom() ?? Configuration.SendDom)
            {
                try
                {
                    string domJson = TryCaptureDom();
                    domUrl = TryPostDomCapture_(domJson);
                    Logger.Verbose("domUrl: {0}", domUrl);
                }
                catch (Exception ex)
                {
                    Logger.Log("Error: " + ex);
                }
            }

            return domUrl;
        }

        public Dictionary<IosDeviceName, DeviceSize> GetIosDevicesSizes()
        {
            if (iosDevicesSizes_ != null)
            {
                return iosDevicesSizes_;
            }
            iosDevicesSizes_ = ((ServerConnector)ServerConnector).GetIosDevicesSizes();
            return iosDevicesSizes_;
        }

        public Dictionary<DeviceName, DeviceSize> GetEmulatedDevicesSizes()
        {
            if (emulatedDevicesSizes_ != null)
            {
                return emulatedDevicesSizes_;
            }
            emulatedDevicesSizes_ = ((ServerConnector)ServerConnector).GetEmulatedDevicesSizes();
            return emulatedDevicesSizes_;
        }

        public Dictionary<BrowserType, string> GetUserAgents()
        {
            if (userAgents_ != null)
            {
                return userAgents_;
            }
            userAgents_ = ((ServerConnector)ServerConnector).GetUserAgents();
            return userAgents_;
        }

        #endregion

        #endregion
    }
}
