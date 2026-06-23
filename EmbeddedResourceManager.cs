using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace ScourgifyMini
{
    internal sealed class EmbeddedResourceManager : ResourceManager
    {
        private readonly Assembly _assembly;
        private readonly string _baseName;
        private readonly Dictionary<string, Dictionary<string, string>> _localizedResources =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public EmbeddedResourceManager(string baseName, Assembly assembly)
            : base(baseName, assembly)
        {
            _baseName = baseName;
            _assembly = assembly;
        }

        public override string GetString(string name, CultureInfo culture)
        {
            if (string.IsNullOrEmpty(name))
                return base.GetString(name, culture);

            CultureInfo currentCulture = culture ?? CultureInfo.CurrentUICulture;
            while (currentCulture != null && !string.IsNullOrEmpty(currentCulture.Name))
            {
                string value;
                if (TryGetLocalizedString(currentCulture.Name, name, out value))
                    return value;

                currentCulture = currentCulture.Parent;
            }

            return base.GetString(name, CultureInfo.InvariantCulture);
        }

        private bool TryGetLocalizedString(string cultureName, string name, out string value)
        {
            Dictionary<string, string> resources = GetLocalizedResources(cultureName);
            if (resources != null && resources.TryGetValue(name, out value))
                return true;

            value = null;
            return false;
        }

        private Dictionary<string, string> GetLocalizedResources(string cultureName)
        {
            Dictionary<string, string> resources;
            if (_localizedResources.TryGetValue(cultureName, out resources))
                return resources;

            resources = LoadLocalizedResources(cultureName);
            _localizedResources[cultureName] = resources;
            return resources;
        }

        private Dictionary<string, string> LoadLocalizedResources(string cultureName)
        {
            string resourceName = _baseName + "." + cultureName + ".resources";
            using (var stream = _assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    return null;

                var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var reader = new ResourceReader(stream))
                {
                    foreach (System.Collections.DictionaryEntry entry in reader)
                    {
                        if (entry.Value is string value)
                            resources[(string)entry.Key] = value;
                    }
                }

                return resources;
            }
        }
    }
}
