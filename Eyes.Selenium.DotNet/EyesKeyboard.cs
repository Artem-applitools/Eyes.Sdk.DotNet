﻿namespace Applitools.Selenium
{
    using System;
    using System.Drawing;
    using Applitools.Utils;
    using OpenQA.Selenium;
    using OpenQA.Selenium.Remote;

    /// <summary>
    /// A wrapper class for Selenium's Keyboard interface, so we can record keyboard
    /// events.
    /// </summary>
    [Obsolete]
    public class EyesKeyboard : IKeyboard
    {
        #region Fields

        private readonly EyesWebDriver eyesDriver_;
        private readonly IKeyboard keyboard_;

        #endregion

        #region Constructors

        public EyesKeyboard(Logger logger, EyesWebDriver eyesDriver, IKeyboard keyboard)
        {
            ArgumentGuard.NotNull(logger, nameof(logger));
            ArgumentGuard.NotNull(eyesDriver, nameof(eyesDriver));
            ArgumentGuard.NotNull(keyboard, nameof(keyboard));

            Logger = logger;
            eyesDriver_ = eyesDriver;
            keyboard_ = keyboard;
        }

        #endregion

        #region Properties

        protected Logger Logger { get; private set; }

        #endregion

        #region Methods

        public void SendKeys(string keySequence)
        {
            var control = Rectangle.Empty;

            // We try to use the active element to get the region.
            IWebElement activeElement = eyesDriver_.SwitchTo().ActiveElement();
            if (activeElement is RemoteWebElement)
            {
                activeElement = new EyesRemoteWebElement(Logger, eyesDriver_, activeElement);
                control = ((EyesRemoteWebElement)activeElement).GetBounds().ToRectangle();
            }

            eyesDriver_.Eyes.AddKeyboardTrigger(control, keySequence);
            keyboard_.SendKeys(keySequence);
        }

        public void PressKey(string keyToPress)
        {
            keyboard_.PressKey(keyToPress);
        }

        public void ReleaseKey(string keyToRelease)
        {
            keyboard_.ReleaseKey(keyToRelease);
        }

        #endregion
    }
}
