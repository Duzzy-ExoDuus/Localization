﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;

namespace NeoSmart.Localization
{
	public class LocaleManager
	{
		private static string _transFolder = @"";
		private static string _defaultLocale = @"";
		private static string _propertiesXml = @"";
		private static string _currentLocaleKey = @"";
	    private Locale _currentLocale = null;

		private static readonly Dictionary<string, Locale> LocalesMap = new Dictionary<string, Locale>();

		public string CurrentLocale { get { return _currentLocaleKey; } private set { _currentLocaleKey = value; } }

		public string DefaultCollectionKey { get; set; }

		public string DefaultLocale
		{
			get { return _defaultLocale; }
			set { lock (_defaultLocale) _defaultLocale = value; }
		}

		public Dictionary<string, Locale> Locales
		{
			get { return LocalesMap; }
		}

		public string PropertiesXml { get { return _propertiesXml; } }

		public string LocaleRoot
		{
			get { return _transFolder; }
			set
			{
				lock (_transFolder)
				{
					_transFolder = Path.IsPathRooted(value) ? value : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, value);
				}
			}
		}

	    public bool EnableAutoRtl { get; set; } = true;

		public LocaleManager(string defaultCollectionKey = null, string defaultLocale = @"en-US", string localizationFolder = @"lang",
		                     string propertiesXml = @"properties.xml")
		{
			if(string.IsNullOrEmpty(LocaleRoot))
			{
				LocaleRoot = localizationFolder;
			}

			lock (_propertiesXml)
			{
				if (string.IsNullOrEmpty(_propertiesXml))
				{
					_propertiesXml = propertiesXml;
				}
			}

			lock (_defaultLocale)
			{
				if (string.IsNullOrEmpty(_defaultLocale))
				{
					_defaultLocale = defaultLocale;
				}
			}

			DefaultCollectionKey = defaultCollectionKey;
		}

		public void LoadLocales()
		{
			lock (Locales)
			{
				LocalesMap.Clear();
				var directories = Directory.GetDirectories(_transFolder);
				foreach (var directory in directories)
				{
					var localeKey = Path.GetFileName(directory);
					if (string.IsNullOrEmpty(localeKey))
						continue;

					var propertiesXml = Path.Combine(directory, _propertiesXml);

					if (!File.Exists(propertiesXml))
						continue;

					var locale = new Locale(localeKey);
					locale.LoadProperties(propertiesXml);

					LocalesMap.Add(localeKey, locale);
				}
			}
		}

        private void LocalizeToolstrip(ToolStripItem child)
        {
            child.Text = GetString(child.Name, true, child.Text);
            if (child is ToolStripMenuItem)
            {
                foreach (var submenu in (child as ToolStripMenuItem).DropDownItems)
                {
                    if (submenu is ToolStripItem)
                    {
                        LocalizeToolstrip(submenu as ToolStripItem);
                    }
                }
            }
        }

        public void Localize(Control control)
        {
            if (_currentLocale == null)
            {
                _currentLocale = LocalesMap[CurrentLocale];
            }

            foreach (Control child in control.Controls)
            {
                child.Text = GetString(child.Name, true, child.Text);
                Localize(child);
            }

            var toolStrip = control as ToolStrip;
            if (toolStrip != null)
            {
                foreach (ToolStripItem child in toolStrip.Items)
                {
                    LocalizeToolstrip(child);
                }
            }

            var comboBox = control as ComboBox;
            if (comboBox != null)
            {
                try
                {
                    var translated = GetString(comboBox.Name);
                    comboBox.Items.Clear();
                    comboBox.Items.AddRange(translated.Value.Split(new []{'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries));
                }
                catch (StringNotFoundException)
                {
                    //Do nothing
                }
            }

            //[EBCD-508]: Automatically set RTL of all form elements
            if (EnableAutoRtl)
            {
                control.RightToLeft = _currentLocale.RightToLeft ? RightToLeft.Yes : RightToLeft.No;
            }
        }

		public bool SetLocale(string localeKey)
		{
			lock (Locales)
			{
				if (!LocalesMap.ContainsKey(localeKey))
					return false;
			}

			CurrentLocale = localeKey;

			LoadLocale(localeKey);

			if (localeKey != _defaultLocale)
			{
				if (!LoadLocale(_defaultLocale))
					throw new DefaultLocaleNotFoundException();

				if(string.IsNullOrEmpty(Locales[CurrentLocale].ParentLocale))
				{
					Locales[CurrentLocale].ParentLocale = DefaultLocale;
				}
			}

			return true;
		}

		private bool LoadLocale(string localeKey)
		{
			var localeFolder = Path.Combine(_transFolder, localeKey);
			if (!Directory.Exists(localeFolder))
				return false;

			if (Locales.Count == 0)
			{
				LoadLocales();
			}

			bool result = LocalesMap[localeKey].Load(Path.Combine(localeFolder, _propertiesXml));

			return result;
		}

		public StringStatus GetStringStatus(Locale locale, string collectionKey, string key)
		{
			StringTranslation translation;

			if (!locale.StringCollections[collectionKey].StringsTable.TryGetValue(key, out translation))
				return StringStatus.Missing;

			if (translation.DeriveFromParent)
				return StringStatus.UpToDate;

			if (translation.AliasedKey)
				return StringStatus.UpToDate;

			if (translation.Version == GetLocaleVersion(_defaultLocale, collectionKey, key))
				return StringStatus.UpToDate;

            if (string.IsNullOrEmpty(translation.Value))
                return StringStatus.Missing;

			return StringStatus.Outdated;
		}

		public uint GetLocaleVersion(string localeKey, string collectionKey, string key)
		{
			return LocalesMap[localeKey].StringCollections[collectionKey].StringsTable[key].Version;
		}

		public StringTranslation GetString(string key, bool useFallback = false, string fallback = null)
		{
		    CheckDefaultCollectionKey();
			return GetString(DefaultCollectionKey, key, useFallback, fallback);
		}

		public StringTranslation GetString(string collectionKey, string key, bool useFallback = false, string fallback = null)
		{
			var localeKey = string.IsNullOrEmpty(CurrentLocale) ? _defaultLocale : CurrentLocale;
			return GetString(localeKey, collectionKey, key, useFallback ? fallback : null);
		}

		public StringTranslation GetString(string localeKey, string collectionKey, string key, string fallback = null)
		{
			if (!LocalesMap.ContainsKey(localeKey))
			{
				LoadLocale(localeKey);
			}

			var locale = LocalesMap[localeKey];

			while (true)
			{
				try
				{
					var translation = locale.GetString(collectionKey, key);
					if (translation.AliasedKey)
					{
						//We've come across a string in a parent locale that's actually linked
						//Try to grab the clone source from the child instead
						return GetString(localeKey, collectionKey, translation.CloneOf, fallback);
					}
					if (translation.DeriveFromParent)
					{
						//Keep things simple :)
						throw new KeyNotFoundException();
					}
					return translation;
				}
				catch (KeyNotFoundException)
				{
					if (!string.IsNullOrEmpty(locale.ParentLocale))
					{
						locale = LocalesMap[locale.ParentLocale];
						continue;
					}

					if (fallback != null)
					{
						return new StringTranslation(key, fallback);
					}

					throw new StringNotFoundException(key, collectionKey, localeKey);
				}
			}
		}

		private void CheckDefaultCollectionKey()
		{
			if(DefaultCollectionKey == null)
			{
				throw new DefaultCollectionKeyNotSet("An attempt to load keys without specifying the collection key was made, and no default collection key was previously set.");
			}
		}

		public StringTranslation[] GetStrings(string[] keys)
		{
			CheckDefaultCollectionKey();
			return GetStrings(DefaultCollectionKey, keys);
		}

		public StringTranslation[] GetStrings(string collectionKey, string[] keys)
		{
			var results = new List<StringTranslation>(keys.Length);

			foreach(string key in keys)
			{
				results.Add(GetString(collectionKey, key));
			}

			return results.ToArray();
		}
	}
}
