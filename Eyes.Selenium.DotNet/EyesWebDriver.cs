﻿namespace Applitools.Selenium
{
    using OpenQA.Selenium;
    using OpenQA.Selenium.Internal;
    using OpenQA.Selenium.Remote;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics.CodeAnalysis;
    using Utils;
    using Size = System.Drawing.Size;

    public sealed class EyesWebDriver :
        IHasCapabilities,
#pragma warning disable CS0618 // Type or member is obsolete
        IHasInputDevices,
#pragma warning restore CS0618 // Type or member is obsolete
        IFindsByClassName, IFindsByCssSelector,
        IFindsById, IFindsByLinkText, IFindsByName, IFindsByTagName, IFindsByXPath,
        IJavaScriptExecutor, ISearchContext, ITakesScreenshot, IWebDriver, IEyesJsExecutor
    {
        #region Fields

        private const int MaxScrollBarSize_ = 50;
        private const int MinScreenshotPartHeight_ = 10;
        private Size defaultContentViewportSize_;
        private ITargetLocator targetLocator_;
        private readonly FrameChain frameChain_;

        #endregion

        #region Constructors

        internal EyesWebDriver(Logger logger, SeleniumEyes eyes, RemoteWebDriver driver)
        {
            ArgumentGuard.NotNull(logger, nameof(logger));
            //ArgumentGuard.NotNull(eyes, nameof(eyes));
            ArgumentGuard.NotNull(driver, nameof(driver));

            Logger_ = logger;
            Eyes = eyes;
            RemoteWebDriver = driver;
            frameChain_ = new FrameChain(logger);

            Logger_.Verbose("Driver is {0}", driver.GetType());
        }

        public FrameChain GetFrameChain()
        {
            return frameChain_;
        }

        #endregion

        #region Properties

        internal SeleniumEyes Eyes { get; private set; }

        public RemoteWebDriver RemoteWebDriver { get; internal set; }

        public string Title
        {
            get { return RemoteWebDriver.Title; }
        }

        public string PageSource
        {
            get { return RemoteWebDriver.PageSource; }
        }

        public ReadOnlyCollection<string> WindowHandles
        {
            get { return RemoteWebDriver.WindowHandles; }
        }

        public string CurrentWindowHandle
        {
            get { return RemoteWebDriver.CurrentWindowHandle; }
        }

        [Obsolete]
        public IMouse Mouse
        {
            get { return new EyesMouse(Logger_, this, RemoteWebDriver.Mouse); }
        }

        [Obsolete]
        public IKeyboard Keyboard
        {
            get { return new EyesKeyboard(Logger_, this, RemoteWebDriver.Keyboard); }
        }

        public ICapabilities Capabilities
        {
            get { return RemoteWebDriver.Capabilities; }
        }

        public string Url
        {
            get { return RemoteWebDriver.Url; }
            set { RemoteWebDriver.Url = value; }
        }

        private Logger Logger_ { get; set; }

        #endregion

        #region Methods

        public string GetUserAgent()
        {
            string userAgent = null;

            try
            {
                userAgent = (string)JSBrowserCommands.WithReturn.GetUserAgent((s) => ExecuteScript(s));

                Logger_.Log(userAgent);
            }
            catch (Exception)
            {
                Logger_.Log("Failed to obtain user-agent string");
            }

            return userAgent;
        }

        public ReadOnlyCollection<IWebElement> FindElements(By by)
        {
            IEnumerable<IWebElement> foundWebElementsList = RemoteWebDriver.FindElements(by);

            // This list will contain the found elements wrapped with our class.
            List<IWebElement> eyesWebElementsList = new List<IWebElement>();

            // TODO - Daniel, Support additional implementation of web element
            foreach (IWebElement element in foundWebElementsList)
            {
                if (element is RemoteWebElement && !(element is EyesRemoteWebElement))
                {
                    eyesWebElementsList.Add(new EyesRemoteWebElement(Logger_, this, element));
                }
                else
                {
                    eyesWebElementsList.Add(element);
                }
            }

            return eyesWebElementsList.AsReadOnly();
        }

        public IWebElement FindElement(By by)
        {
            // TODO - Daniel, support additional implementations of WebElement.
            IWebElement webElement = RemoteWebDriver.FindElement(by);
            if (webElement is RemoteWebElement remoteWebElement && !(webElement is EyesRemoteWebElement))
            {
                webElement = new EyesRemoteWebElement(Logger_, this, remoteWebElement);
            }

            return webElement;
        }

        public void Close()
        {
            RemoteWebDriver.Close();
        }

        public void Quit()
        {
            RemoteWebDriver.Quit();
        }

        public ITargetLocator SwitchTo()
        {
            if (targetLocator_ == null)
            {
                targetLocator_ = new EyesWebDriverTargetLocator(this, Logger_, RemoteWebDriver.SwitchTo());
            }
            return targetLocator_;
        }

        private class EyesWebDriverNavigation : INavigation
        {
            private INavigation navigation_;
            private FrameChain frameChain_;
            public EyesWebDriverNavigation(INavigation navigation, FrameChain frameChain)
            {
                navigation_ = navigation;
                frameChain_ = frameChain;
            }

            public void Back()
            {
                navigation_.Back();
                frameChain_.Clear();
            }

            public void Forward()
            {
                navigation_.Forward();
                frameChain_.Clear();
            }

            public void GoToUrl(Uri url)
            {
                navigation_.GoToUrl(url);
                frameChain_.Clear();
            }

            public void GoToUrl(string url)
            {
                navigation_.GoToUrl(url);
                frameChain_.Clear();
            }

            public void Refresh()
            {
                navigation_.Refresh();
                frameChain_.Clear();
            }
        }

        public INavigation Navigate()
        {
            return new EyesWebDriverNavigation(RemoteWebDriver.Navigate(), frameChain_);
        }

        public IOptions Manage()
        {
            return RemoteWebDriver.Manage();
        }

        public IWebElement FindElementByClassName(string className)
        {
            return FindElement(By.ClassName(className));
        }

        public ReadOnlyCollection<IWebElement> FindElementsByClassName(string className)
        {
            return FindElements(By.ClassName(className));
        }

        public IWebElement FindElementByCssSelector(string cssSelector)
        {
            return FindElement(By.CssSelector(cssSelector));
        }

        public ReadOnlyCollection<IWebElement> FindElementsByCssSelector(string cssSelector)
        {
            return FindElements(By.CssSelector(cssSelector));
        }

        public IWebElement FindElementById(string id)
        {
            return FindElement(By.Id(id));
        }

        public ReadOnlyCollection<IWebElement> FindElementsById(string id)
        {
            return FindElements(By.Id(id));
        }

        public IWebElement FindElementByLinkText(string linkText)
        {
            return FindElement(By.LinkText(linkText));
        }

        public ReadOnlyCollection<IWebElement> FindElementsByLinkText(string linkText)
        {
            return FindElements(By.LinkText(linkText));
        }

        [SuppressMessage(
            "Microsoft.Performance",
            "CA1811:AvoidUncalledPrivateCode",
            Justification = "Serialization required")]
        public IWebElement FindElementByPartialLinkText(string partialLinkText)
        {
            return FindElement(By.PartialLinkText(partialLinkText));
        }

        public IWebElement FindElementByName(string name)
        {
            return FindElement(By.Name(name));
        }

        public ReadOnlyCollection<IWebElement> FindElementsByName(string name)
        {
            return FindElements(By.Name(name));
        }

        public IWebElement FindElementByTagName(string tagName)
        {
            return FindElement(By.TagName(tagName));
        }

        public ReadOnlyCollection<IWebElement> FindElementsByTagName(string tagName)
        {
            return FindElements(By.TagName(tagName));
        }

        public IWebElement FindElementByXPath(string xpath)
        {
            return FindElement(By.XPath(xpath));
        }

        public ReadOnlyCollection<IWebElement> FindElementsByXPath(string xpath)
        {
            return FindElements(By.XPath(xpath));
        }

        public object ExecuteScript(string script, params object[] args)
        {
            return RemoteWebDriver.ExecuteScript(script, args);
        }

        public object ExecuteAsyncScript(string script, params object[] args)
        {
            return RemoteWebDriver.ExecuteAsyncScript(script, args);
        }

        /// <summary>
        /// Returns the viewport size of the default content (outer most frame).
        /// </summary>
        /// <param name="forceQuery">If true, we will perform the query even if we have a cached viewport size.</param>
        /// <returns>The viewport size of the default content (outer most frame).</returns>
        public Size GetDefaultContentViewportSize(bool forceQuery = false)
        {
            Logger_.Verbose("GetDefaultContentViewportSize()");

            if (!defaultContentViewportSize_.IsEmpty && !forceQuery)
            {
                Logger_.Verbose("Using cached viewport size: {0}", defaultContentViewportSize_);
                return defaultContentViewportSize_;
            }

            FrameChain currentFrames = frameChain_.Clone();
            if (currentFrames.Count > 0)
            {
                SwitchTo().DefaultContent();
            }

            Logger_.Verbose("Extracting viewport size...");
            defaultContentViewportSize_ = EyesSeleniumUtils.GetViewportSizeOrDisplaySize(Logger_, this);
            Logger_.Verbose("Done! Viewport size: {0}", defaultContentViewportSize_);

            if (currentFrames.Count > 0)
            {
                ((EyesWebDriverTargetLocator)SwitchTo()).Frames(currentFrames);
            }
            return defaultContentViewportSize_;
        }

        public Screenshot GetScreenshot()
        {
            return ((ITakesScreenshot)RemoteWebDriver).GetScreenshot();
        }

        public void Dispose()
        {
            RemoteWebDriver.Dispose();
        }

        #endregion
    }
}