﻿using Applitools.Metadata;
using Applitools.Selenium.Tests.Utils;
using Applitools.Selenium.VisualGrid;
using Applitools.Tests.Utils;
using Applitools.Ufg;
using Applitools.Utils;
using Applitools.VisualGrid;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Remote;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using Region = Applitools.Utils.Geometry.Region;

namespace Applitools.Selenium.Tests
{
    public abstract class TestSetup : ReportingTestSuite
    {
        public class Expectations
        {
            public Dictionary<string, object> ExpectedProperties { get; set; } = new Dictionary<string, object>();
            public HashSet<Region> ExpectedIgnoreRegions { get; set; } = new HashSet<Region>();
            public HashSet<Region> ExpectedStrictRegions { get; set; } = new HashSet<Region>();
            public HashSet<Region> ExpectedLayoutRegions { get; set; } = new HashSet<Region>();
            public HashSet<Region> ExpectedContentRegions { get; set; } = new HashSet<Region>();
            public HashSet<FloatingMatchSettings> ExpectedFloatingRegions { get; set; } = new HashSet<FloatingMatchSettings>();
            public HashSet<AccessibilityRegionByRectangle> ExpectedAccessibilityRegions { get; internal set; } = new HashSet<AccessibilityRegionByRectangle>();
        }

        class SpecificTestContextRequirements
        {
            public SpecificTestContextRequirements(Eyes eyes, string testName)
            {
                Eyes = eyes;
                TestName = testName;
            }

            public Eyes Eyes { get; private set; }
            public IWebDriver WrappedDriver { get; set; }
            public IWebDriver WebDriver { get; set; }
            public Dictionary<int, Expectations> Expectations = new Dictionary<int, Expectations>();
            public string TestName { get; }
            public string TestNameAsFilename { get; set; }
            public string ExpectedVGOutput { get; set; }
        }

        protected DriverOptions options_;
        protected readonly bool useVisualGrid_;
        protected readonly StitchModes stitchMode_;
        private string testSuitName_;
        protected string testedPageUrl = "https://applitools.github.io/demo/TestPages/FramesTestPage/";
        protected string seleniumServerUrl = null;
        protected Size testedPageSize = new Size(700, 460);

        private string testNameSuffix_ = Environment.GetEnvironmentVariable("TEST_NAME_SUFFIX");


        protected bool CompareExpectedRegion { get; set; } = true;

        private IDictionary<string, SpecificTestContextRequirements> testDataByTestId_ = new ConcurrentDictionary<string, SpecificTestContextRequirements>();

        public TestSetup(string testSuitName)
        {
            testSuitName_ = testSuitName + testNameSuffix_;
        }

        public TestSetup(string testSuitName, DriverOptions options, bool useVisualGrid = false)
        {
            testSuitName_ = testSuitName + testNameSuffix_;
            options_ = options;
            useVisualGrid_ = useVisualGrid;
            stitchMode_ = StitchModes.CSS;
            suiteArgs_.Add("mode", "VisualGrid");
        }

        public TestSetup(string testSuitName, DriverOptions options, StitchModes stitchMode = StitchModes.CSS)
        {
            testSuitName_ = testSuitName + testNameSuffix_;
            options_ = options;
            useVisualGrid_ = false;
            stitchMode_ = stitchMode;
            suiteArgs_.Add("mode", stitchMode.ToString());
        }

        #region expected regions

        private Expectations GetExpectationsAtIndex(int index)
        {
            SpecificTestContextRequirements testData = testDataByTestId_[TestContext.CurrentContext.Test.ID];
            if (!testData.Expectations.TryGetValue(index, out Expectations expectations))
            {
                expectations = new Expectations();
                testData.Expectations[index] = expectations;
            }
            return expectations;
        }

        protected void SetExpectedAccessibilityRegions(params AccessibilityRegionByRectangle[] accessibilityRegions)
        {
            SetExpectedAccessibilityRegions(0, accessibilityRegions);
        }

        protected void SetExpectedAccessibilityRegions(int index, params AccessibilityRegionByRectangle[] accessibilityRegions)
        {
            GetExpectationsAtIndex(index).ExpectedAccessibilityRegions = new HashSet<AccessibilityRegionByRectangle>(accessibilityRegions);
        }

        protected void SetExpectedFloatingRegions(params FloatingMatchSettings[] floatingMatchSettings)
        {
            SetExpectedFloatingRegions(0, floatingMatchSettings);
        }

        protected void SetExpectedFloatingRegions(int index, params FloatingMatchSettings[] floatingMatchSettings)
        {
            GetExpectationsAtIndex(index).ExpectedFloatingRegions = new HashSet<FloatingMatchSettings>(floatingMatchSettings);
        }

        protected void SetExpectedIgnoreRegions(params Region[] ignoreRegions)
        {
            SetExpectedIgnoreRegions(0, ignoreRegions);
        }

        protected void SetExpectedIgnoreRegions(int index, params Region[] ignoreRegions)
        {
            GetExpectationsAtIndex(index).ExpectedIgnoreRegions = new HashSet<Region>(ignoreRegions);
        }

        protected void SetExpectedLayoutRegions(params Region[] layoutRegions)
        {
            SetExpectedLayoutRegions(0, layoutRegions);
        }

        protected void SetExpectedLayoutRegions(int index, params Region[] layoutRegions)
        {
            GetExpectationsAtIndex(index).ExpectedLayoutRegions = new HashSet<Region>(layoutRegions);
        }

        protected void SetExpectedStrictRegions(params Region[] strictRegions)
        {
            SetExpectedStrictRegions(0, strictRegions);
        }

        protected void SetExpectedStrictRegions(int index, params Region[] strictRegions)
        {
            GetExpectationsAtIndex(index).ExpectedStrictRegions = new HashSet<Region>(strictRegions);
        }

        protected void SetExpectedContentRegions(params Region[] contentRegions)
        {
            SetExpectedContentRegions(0, contentRegions);
        }

        protected void SetExpectedContentRegions(int index, params Region[] contentRegions)
        {
            GetExpectationsAtIndex(index).ExpectedContentRegions = new HashSet<Region>(contentRegions);
        }

        #endregion

        private void Init_(string testName)
        {
            // Initialize the eyes SDK and set your private API key.
            Eyes eyes = InitEyes_();

            string testNameWithArguments = testName;
            foreach(object argValue in TestContext.CurrentContext.Test.Arguments)
            {
                testNameWithArguments += "_" + argValue;
            }

            if (eyes.runner_ is VisualGridRunner)
            {
                testName += "_VG";
                testNameWithArguments += "_VG";
            }
            else if (stitchMode_ == StitchModes.Scroll)
            {
                testName += "_Scroll";
                testNameWithArguments += "_Scroll";
            }

            TestUtils.SetupLogging(eyes, testNameWithArguments);
            eyes.Logger.Log("initializing test: {0}", TestContext.CurrentContext.Test.FullName);
            SpecificTestContextRequirements testContextReqs = new SpecificTestContextRequirements(eyes, testName);
            testDataByTestId_.Add(TestContext.CurrentContext.Test.ID, testContextReqs);

            if ((eyes.runner_ is VisualGridRunner && RUNS_ON_CI) || USE_MOCK_VG)
            {
                eyes.Logger.Log("using VG mock eyes connector");
                string testNameAsFilename = TestUtils.SanitizeForFilename(TestContext.CurrentContext.Test.FullName);
                testContextReqs.TestNameAsFilename = testNameAsFilename;
                Assembly thisAssembly = Assembly.GetCallingAssembly();
                Stream expectedOutputJsonStream = thisAssembly.GetManifestResourceStream("Test.Eyes.Selenium.DotNet.Resources.VGTests." + testNameAsFilename + ".json");
                if (expectedOutputJsonStream != null)
                {
                    using (StreamReader reader = new StreamReader(expectedOutputJsonStream))
                    {
                        testContextReqs.ExpectedVGOutput = reader.ReadToEnd();
                    }
                    eyes.visualGridEyes_.EyesConnectorFactory = new Mock.MockEyesConnectorFactory();
                }
            }
            else
            {
                eyes.Logger.Log("using regular VG eyes connector");
            }

            string seleniumServerUrl = SetupSeleniumServer(testName);
            bool isWellFormedUri = Uri.IsWellFormedUriString(seleniumServerUrl, UriKind.Absolute);

            RemoteWebDriver webDriver = SeleniumUtils.RetryCreateWebDriver(() =>
            {
                RemoteWebDriver driver = null;
                if (isWellFormedUri)
                {
                    try
                    {
                        eyes.Logger.Log("Trying to create RemoteWebDriver on {0}", seleniumServerUrl);
                        driver = new RemoteWebDriver(new Uri(seleniumServerUrl), options_.ToCapabilities(), TimeSpan.FromMinutes(4));
                    }
                    catch (Exception e)
                    {
                        eyes.Logger.Log("Failed creating RemoteWebDriver on {0}. Creating local WebDriver.", seleniumServerUrl);
                        eyes.Logger.Log("Exception: " + e);
                    }
                }

                if (driver != null) return driver;

                if (TestUtils.RUNS_ON_CI)
                {
                    if (options_.BrowserName.Equals(BrowserNames.Chrome, StringComparison.OrdinalIgnoreCase) ||
                        options_.BrowserName.Equals(BrowserNames.Firefox, StringComparison.OrdinalIgnoreCase))
                    {
                        eyes.Logger.Log("webdriver is null, running on a CI and trying to initialize {0} browser.", options_.BrowserName);
                        driver = (RemoteWebDriver)SeleniumUtils.CreateWebDriver(options_);
                    }
                }
                else
                {
                    eyes.Logger.Log("webdriver is null, running locally and trying to initialize {0}.", options_.BrowserName);
                    driver = (RemoteWebDriver)SeleniumUtils.CreateWebDriver(options_);
                }

                return driver;
            });

            eyes.AddProperty("Selenium Session ID", webDriver.SessionId.ToString());

            eyes.AddProperty("ForceFPS", eyes.ForceFullPageScreenshot ? "true" : "false");
            eyes.AddProperty("Agent ID", eyes.FullAgentId);

            //IWebDriver webDriver = new RemoteWebDriver(new Uri("http://localhost:4444/wd/hub"), capabilities_);

            eyes.Logger.Log("navigating to URL: " + testedPageUrl);

            IWebDriver driver;
            try
            {
                BeforeOpen(eyes);
                driver = eyes.Open(webDriver, testSuitName_, testName, testedPageSize);
            }
            catch
            {
                webDriver.Quit();
                throw;
            }

            //string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            driver.Navigate().GoToUrl(testedPageUrl);
            eyes.Logger.Log($"{testName} ({options_.BrowserName}) : {TestDataProvider.BatchInfo.Name}");

            testDataByTestId_[TestContext.CurrentContext.Test.ID].WrappedDriver = driver;
            testDataByTestId_[TestContext.CurrentContext.Test.ID].WebDriver = webDriver;
        }

        protected virtual void BeforeOpen(Eyes eyes) { }

        public void AddExpectedProperty(string propertyName, object expectedValue)
        {
            AddExpectedProperty(0, propertyName, expectedValue);
        }

        public void AddExpectedProperty(int index, string propertyName, object expectedValue)
        {
            Dictionary<string, object> expectedProps = GetExpectationsAtIndex(index).ExpectedProperties;
            expectedProps.Add(propertyName, expectedValue);
        }

        public Eyes GetEyes()
        {
            testDataByTestId_.TryGetValue(TestContext.CurrentContext.Test.ID, out SpecificTestContextRequirements testData);
            Eyes eyes = testData?.Eyes;
            return eyes;
        }

        public IWebDriver GetDriver()
        {
            testDataByTestId_.TryGetValue(TestContext.CurrentContext.Test.ID, out SpecificTestContextRequirements testData);
            IWebDriver driver = testData?.WrappedDriver;
            return driver;
        }

        public IWebDriver GetWebDriver()
        {
            testDataByTestId_.TryGetValue(TestContext.CurrentContext.Test.ID, out SpecificTestContextRequirements testData);
            IWebDriver driver = testData?.WebDriver;
            return driver;
        }

        private Eyes InitEyes_(bool? forceFullPageScreenshot = null)
        {
            EyesRunner runner = useVisualGrid_ ? (EyesRunner)new VisualGridRunner(10) : new ClassicRunner();
            Eyes eyes = new Eyes(runner);

            string serverUrl = Environment.GetEnvironmentVariable("APPLITOOLS_SERVER_URL");
            if (!string.IsNullOrEmpty(serverUrl))
            {
                eyes.ServerUrl = serverUrl;
            }

            if (forceFullPageScreenshot != null)
            {
                eyes.ForceFullPageScreenshot = forceFullPageScreenshot.Value;
            }

            eyes.HideScrollbars = true;
            eyes.StitchMode = stitchMode_;
            eyes.SaveNewTests = false;
            eyes.Batch = TestDataProvider.BatchInfo;

            return eyes;
        }

        private string SetupSeleniumServer(string testName)
        {
            if (TestUtils.RUNS_ON_TRAVIS)
            {
                if (options_.BrowserName != "chrome" && options_.BrowserName != "firefox")
                {
                    Dictionary<string, object> sauceOptions = new Dictionary<string, object>
                    {
                        ["username"] = TestDataProvider.SAUCE_USERNAME,
                        ["accesskey"] = TestDataProvider.SAUCE_ACCESS_KEY,
                        ["screenResolution"] = "1920x1080",
                        ["name"] = testName + $" ({GetEyes().FullAgentId})",
                        ["idleTimeout"] = 360
                    };

                    if (options_ is OpenQA.Selenium.IE.InternetExplorerOptions ieOptions)
                    {
                        ieOptions.AddAdditionalCapability("sauce:options", sauceOptions, true);
                        return TestDataProvider.SAUCE_SELENIUM_URL;
                    }

                    if (options_ is OpenQA.Selenium.Safari.SafariOptions safariOptions)
                    {
                        safariOptions.AddAdditionalCapability("sauce:options", sauceOptions);
                        return TestDataProvider.SAUCE_SELENIUM_URL;
                    }

                }
            }
            string seleniumServerUrl = this.seleniumServerUrl ?? Environment.GetEnvironmentVariable("SELENIUM_SERVER_URL");
            if (seleniumServerUrl != null)
            {
                if (seleniumServerUrl.Contains("ondemand.saucelabs.com", StringComparison.OrdinalIgnoreCase))
                {
                    Dictionary<string, object> sauceOptions = new Dictionary<string, object>
                    {
                        ["username"] = TestDataProvider.SAUCE_USERNAME,
                        ["accesskey"] = TestDataProvider.SAUCE_ACCESS_KEY,
                        ["name"] = testName + $" ({GetEyes().FullAgentId})",
                        ["idleTimeout"] = 360
                    };
                    if (options_ is OpenQA.Selenium.Chrome.ChromeOptions chromeOptions)
                    {
                        chromeOptions.UseSpecCompliantProtocol = true;
                        chromeOptions.BrowserVersion = "77.0";
                        chromeOptions.AddAdditionalCapability("sauce:options", sauceOptions, true);
                    }
                }
                else if (seleniumServerUrl.Contains("hub-cloud.browserstack.com", StringComparison.OrdinalIgnoreCase))
                {
                    Dictionary<string, object> browserstackOptions = new Dictionary<string, object>
                    {
                        ["userName"] = TestDataProvider.BROWSERSTACK_USERNAME,
                        ["accessKey"] = TestDataProvider.BROWSERSTACK_ACCESS_KEY,
                        ["name"] = testName + $" ({GetEyes().FullAgentId})"
                    };
                    if (options_ is OpenQA.Selenium.Chrome.ChromeOptions chromeOptions)
                    {
                        chromeOptions.UseSpecCompliantProtocol = true;
                        chromeOptions.BrowserVersion = "77.0";
                        chromeOptions.AddAdditionalCapability("bstack:options", browserstackOptions, true);
                    }
                }
            }
            return seleniumServerUrl;
        }

        [OneTimeSetUp]
        public new void OneTimeSetup()
        {
        }

        [SetUp]
        public new void SetUp()
        {
            Init_(TestContext.CurrentContext.Test.MethodName);
        }

        [TearDown]
        public void Teardown()
        {
            try
            {
                testDataByTestId_.TryGetValue(TestContext.CurrentContext.Test.ID, out SpecificTestContextRequirements testData);
                TestResults results = testData?.Eyes.Close();
                if (results != null)
                {
                    SessionResults sessionResults = TestUtils.GetSessionResults(GetEyes().ApiKey, results);

                    if (sessionResults != null)
                    {
                        ActualAppOutput[] actualAppOutput = sessionResults.ActualAppOutput;
                        for (int i = 0; i < actualAppOutput.Length; i++)
                        {
                            Metadata.ImageMatchSettings ims = actualAppOutput[i].ImageMatchSettings;
                            CompareRegions_(ims, i);
                            CompareProperties_(ims, i);
                        }
                    }
                    testData.Eyes.Logger.Log("Mismatches: " + results.Mismatches);
                }
                if (testData?.Eyes.activeEyes_ is VisualGridEyes visualGridEyes && visualGridEyes.eyesConnector_ is Mock.MockEyesConnector mockEyesConnector)
                {
                    RenderRequest[] lastRequests = mockEyesConnector.LastRenderRequests;
                    string serializedRequests = JsonUtils.Serialize(lastRequests);
                    if (!TestUtils.RUNS_ON_CI)
                    {
                        string dateString = DateTime.Now.ToString("yyyy_MM_dd__HH_mm");
                        string directory = Path.Combine(TestUtils.LOGS_PATH, "DotNet", "VGResults", dateString);
                        Directory.CreateDirectory(directory);
                        File.WriteAllText(Path.Combine(directory, testData.TestNameAsFilename + ".json"), serializedRequests);
                    }
                    Assert.AreEqual(testData.ExpectedVGOutput, serializedRequests, "VG Request DOM JSON");
                }
            }
            catch (Exception ex)
            {
                GetEyes()?.Logger?.GetILogHandler()?.Open();
                GetEyes()?.Logger?.Log("Exception: " + ex);
                throw;
            }
            finally
            {
                Logger logger = GetEyes()?.Logger;
                if (logger != null) {
                    logger.GetILogHandler()?.Open();
                    logger.Log("Test finished.");
                    logger.GetILogHandler()?.Close();
                    Thread.Sleep(1000);
                }
                GetEyes()?.Abort();
                GetWebDriver()?.Quit();
            }
        }

        protected override string GetTestName()
        {
            return testDataByTestId_[TestContext.CurrentContext.Test.ID].TestName;
        }

        private void CompareProperties_(Metadata.ImageMatchSettings ims, int index)
        {
            Dictionary<string, object> expectedProps = GetExpectationsAtIndex(index).ExpectedProperties;

            Type imsType = typeof(Metadata.ImageMatchSettings);
            foreach (KeyValuePair<string, object> kvp in expectedProps)
            {
                string propertyNamePath = kvp.Key;
                string[] properties = propertyNamePath.Split('.');

                Type currentType = imsType;
                object currentObject = ims;
                foreach (string propName in properties)
                {
                    PropertyInfo pi = currentType.GetProperty(propName);
                    currentObject = pi.GetValue(currentObject, null);
                    if (currentObject == null) break;
                    currentType = currentObject.GetType();
                }

                Assert.AreEqual(kvp.Value, currentObject);
            }
        }

        private void CompareRegions_(Metadata.ImageMatchSettings ims, int index)
        {
            if (!CompareExpectedRegion)
            {
                return;
            }

            Expectations expectations = GetExpectationsAtIndex(index);
            CompareAccessibilityRegionsList_(ims.Accessibility, expectations.ExpectedAccessibilityRegions, "Accessibility");
            CompareFloatingRegionsList_(ims.Floating, expectations.ExpectedFloatingRegions, "Floating");
            TestUtils.CompareSimpleRegionsList_(ims.Ignore, expectations.ExpectedIgnoreRegions, "Ignore");
            TestUtils.CompareSimpleRegionsList_(ims.Layout, expectations.ExpectedLayoutRegions, "Layout");
            TestUtils.CompareSimpleRegionsList_(ims.Content, expectations.ExpectedContentRegions, "Content");
            TestUtils.CompareSimpleRegionsList_(ims.Strict, expectations.ExpectedStrictRegions, "Strict");
        }


        private static void CompareFloatingRegionsList_(FloatingMatchSettings[] actualRegions, HashSet<FloatingMatchSettings> expectedRegions, string type)
        {
            HashSet<FloatingMatchSettings> expectedRegionsClone = new HashSet<FloatingMatchSettings>(expectedRegions);
            if (expectedRegions.Count > 0)
            {
                foreach (FloatingMatchSettings region in actualRegions)
                {
                    if (!expectedRegionsClone.Remove(region))
                    {
                        Assert.Fail("actual {0} region {1} not found in expected regions list", type, region);
                    }
                }
                Assert.IsEmpty(expectedRegionsClone, "not all expected regions found in actual regions list.", type);
            }
        }

        internal static void CompareAccessibilityRegionsList_(AccessibilityRegionByRectangle[] actualRegions, HashSet<AccessibilityRegionByRectangle> expectedRegions, string type)
        {
            HashSet<AccessibilityRegionByRectangle> expectedRegionsClone = new HashSet<AccessibilityRegionByRectangle>(expectedRegions);
            if (expectedRegions.Count > 0)
            {
                foreach (AccessibilityRegionByRectangle region in actualRegions)
                {
                    if (!expectedRegionsClone.Remove(region))
                    {
                        Assert.Fail("actual {0} region {1} not found in expected regions list", type, region);
                    }
                }
                Assert.IsEmpty(expectedRegionsClone, "not all expected regions found in actual regions list.", type);
            }
        }

    }
}
