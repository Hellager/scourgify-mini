using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Serilog;
using Tomlyn;
using Tomlyn.Model;

namespace ScourgifyMini
{
    internal class Config
    {
        private const string DefaultLanguage = "en-US";

        public string Language { get; set; } = "en-US";
        public bool AutoStart { get; set; } = false;
        public bool NoTraceMode { get; set; } = false;
        public bool CleanupNewRecentLinksOnUnlock { get; set; } = true;

        public static readonly List<LanguageInfo> SupportedLanguages = new List<LanguageInfo>
        {
            new LanguageInfo { Code = "en-US", DisplayName = "English" },
            new LanguageInfo { Code = "zh-CN", DisplayName = "中文(简体)" },
            new LanguageInfo { Code = "zh-TW", DisplayName = "中文(繁體)" },
            new LanguageInfo { Code = "fr-FR", DisplayName = "Français" },
            new LanguageInfo { Code = "ru-RU", DisplayName = "Русский" }
        };

        private static readonly string ConfigPath = Path.Combine(
            // Keep config next to the executable intentionally for portable deployments.
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
            "config.toml");

        internal static string FilePath
        {
            get { return ConfigPath; }
        }

        public static Config Load()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string tomlString = File.ReadAllText(ConfigPath);
                    var tomlTable = TomlSerializer.Deserialize<TomlTable>(tomlString);

                    var config = new Config
                    {
                        Language = GetValueOrDefault<string>(tomlTable, "Language", DefaultLanguage),
                        AutoStart = GetValueOrDefault<bool>(tomlTable, "AutoStart", false),
                        NoTraceMode = GetValueOrDefault(
                            tomlTable,
                            "NoTraceMode",
                            GetValueOrDefault<bool>(tomlTable, "IncognitoMode", false)),
                        CleanupNewRecentLinksOnUnlock = GetValueOrDefault<bool>(
                            tomlTable,
                            "CleanupNewRecentLinksOnUnlock",
                            true)
                    };

                    string normalizedLanguage = NormalizeLanguage(config.Language);
                    if (config.Language != normalizedLanguage)
                    {
                        config.Language = normalizedLanguage;
                        Save(config);
                    }

                    return config;
                }
                catch (System.Exception ex)
                {
                    Log.Warning(ex, "Failed to load config file, creating default config: {ConfigPath}", ConfigPath);
                    return CreateDefaultConfig();
                }
            }

            Log.Information("Config file not found, creating default config: {ConfigPath}", ConfigPath);
            return CreateDefaultConfig();
        }

        private static T GetValueOrDefault<T>(TomlTable table, string key, T defaultValue)
        {
            if (table.ContainsKey(key) && table[key] is T value)
            {
                return value;
            }
            return defaultValue;
        }

        private static Config CreateDefaultConfig()
        {
            var config = new Config
            {
                Language = GetDefaultLanguage()
            };

            Log.Information(
                "Creating default config: ConfigPath={ConfigPath}, Language={Language}",
                ConfigPath,
                config.Language);
            Save(config);
            return config;
        }

        public static bool IsSupportedLanguage(string language)
        {
            return GetSupportedLanguageCode(language) != null;
        }

        public static string NormalizeLanguage(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return GetDefaultLanguage();
            }

            string trimmedLanguage = language.Trim();
            string supportedLanguage = GetSupportedLanguageCode(trimmedLanguage);
            if (supportedLanguage != null)
            {
                return supportedLanguage;
            }

            string lowerLanguage = trimmedLanguage.ToLowerInvariant();
            if (lowerLanguage == "en")
                return "en-US";
            if (lowerLanguage == "fr")
                return "fr-FR";
            if (lowerLanguage == "ru")
                return "ru-RU";
            if (lowerLanguage == "zh")
                return GetDefaultChineseLanguage();
            if (lowerLanguage.StartsWith("zh-"))
                return GetChineseLanguage(trimmedLanguage);

            try
            {
                var culture = CultureInfo.GetCultureInfo(trimmedLanguage);
                if (culture.Name.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase))
                {
                    return GetChineseLanguage(culture.Name);
                }
            }
            catch (CultureNotFoundException)
            {
            }

            return GetDefaultLanguage();
        }

        public static string GetDefaultLanguage()
        {
            var culture = CultureInfo.CurrentUICulture;
            string supportedLanguage = GetSupportedLanguageCode(culture.Name);
            if (supportedLanguage != null)
            {
                return supportedLanguage;
            }

            switch (culture.TwoLetterISOLanguageName.ToLowerInvariant())
            {
                case "zh":
                    return GetChineseLanguage(culture.Name);
                case "fr":
                    return "fr-FR";
                case "ru":
                    return "ru-RU";
                case "en":
                    return "en-US";
                default:
                    return DefaultLanguage;
            }
        }

        private static string GetSupportedLanguageCode(string language)
        {
            foreach (var supportedLanguage in SupportedLanguages)
            {
                if (string.Equals(supportedLanguage.Code, language, System.StringComparison.OrdinalIgnoreCase))
                {
                    return supportedLanguage.Code;
                }
            }

            return null;
        }

        private static string GetDefaultChineseLanguage()
        {
            return GetChineseLanguage(CultureInfo.CurrentUICulture.Name);
        }

        private static string GetChineseLanguage(string cultureName)
        {
            string lowerCultureName = cultureName.ToLowerInvariant();
            if (lowerCultureName == "zh-tw" ||
                lowerCultureName == "zh-hk" ||
                lowerCultureName == "zh-mo" ||
                lowerCultureName.StartsWith("zh-hant"))
            {
                return "zh-TW";
            }

            return "zh-CN";
        }

        public static void Save(Config config)
        {
            try
            {
                config.Language = NormalizeLanguage(config.Language);
                var tomlTable = new TomlTable
                {
                    ["Language"] = config.Language,
                    ["AutoStart"] = config.AutoStart,
                    ["NoTraceMode"] = config.NoTraceMode,
                    ["CleanupNewRecentLinksOnUnlock"] = config.CleanupNewRecentLinksOnUnlock
                };

                string tomlString = TomlSerializer.Serialize(tomlTable);
                File.WriteAllText(ConfigPath, tomlString);
            }
            catch (System.Exception ex)
            {
                Log.Warning(ex, "Failed to save config file: {ConfigPath}", ConfigPath);
            }
        }
    }

    public class LanguageInfo
    {
        public string Code { get; set; }
        public string DisplayName { get; set; }
    }
}
