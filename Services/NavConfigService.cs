using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services
{
    public class NavConfigItem
    {
        public string Tag { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? GeometryKey { get; set; }
        public string? Group { get; set; }
    }

    public static class NavConfigService
    {
        public static IEnumerable<NavMenuItem> LoadFromConfig(string basePath)
        {
            string path = Path.Combine(basePath, "config", "nav.json");
            if (!File.Exists(path))
            {
                yield break;
            }

            var json = File.ReadAllText(path);
            var items = JsonSerializer.Deserialize<List<NavConfigItem>>(json);
            if (items == null)
            {
                yield break;
            }

            foreach (var i in items)
            {
                Geometry? geo = null;
                if (!string.IsNullOrEmpty(i.GeometryKey))
                {
                    var res = System.Windows.Application.Current.TryFindResource(i.GeometryKey);
                    geo = res as Geometry;
                }

                yield return new NavMenuItem
                {
                    Tag = i.Tag,
                    Title = i.Title,
                    GeometryData = geo,
                    Icon = null,
                    Group = string.IsNullOrEmpty(i.Group) ? "Default" : i.Group
                };
            }
        }
    }
}
