using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace WarThunderChatTranslator.Helpers
{
    public class ReleaseAsset
    {
        public string Name { get; set; }
        public int Size { get; set; }
        public string BrowserDownloadUrl { get; set; }
    }

    public class UpdateInfo
    {
        public string TagName { get; set; }
        public DateTimeOffset PublishedAt { get; set; }
        public Uri HtmlUrl { get; set; }
        public bool IsExistNewVersion { get; set; }
        public List<ReleaseAsset> Assets { get; set; } = new List<ReleaseAsset>();
    }

    public static class UpdateHelper
    {
        private static readonly HttpClient client = new HttpClient();

        public static async Task<UpdateInfo> CheckUpdateAsync(string owner, string repo)
        {
            var json = await client.GetStringAsync($"https://api.github.com/repos/{owner}/{repo}/releases/latest");
            var token = JObject.Parse(json);

            var info = new UpdateInfo
            {
                TagName = (string)token["tag_name"],
                HtmlUrl = token["html_url"] != null ? new Uri((string)token["html_url"]) : null,
                PublishedAt = DateTimeOffset.TryParse((string)token["published_at"], out var publishedAt) ? publishedAt : DateTimeOffset.Now
            };

            if (token["assets"] is JArray assets)
            {
                foreach (var asset in assets)
                {
                    info.Assets.Add(new ReleaseAsset
                    {
                        Name = (string)asset["name"],
                        Size = asset["size"] != null ? (int)asset["size"] : 0,
                        BrowserDownloadUrl = (string)asset["browser_download_url"]
                    });
                }
            }

            info.IsExistNewVersion = CompareVersions(info.TagName) > 0;
            return info;
        }

        /// <summary>
        /// 比较远端 tag 版本与当前安装版本。返回 1 表示远端更新，0 相同，-1 远端更旧。
        /// </summary>
        private static int CompareVersions(string tagName)
        {
            var remote = ParseVersion(tagName);
            Version current;
            try
            {
                var v = Package.Current.Id.Version;
                current = new Version(v.Major, v.Minor, v.Build, v.Revision);
            }
            catch
            {
                // 非打包运行场景（如调试），跳过比较
                return 0;
            }

            for (int i = 0; i < 4; i++)
            {
                var r = i < remote.Length ? remote[i] : 0;
                var l = i switch
                {
                    0 => current.Major,
                    1 => current.Minor,
                    2 => current.Build,
                    _ => current.Revision
                };
                if (r > l) return 1;
                if (r < l) return -1;
            }
            return 0;
        }

        private static int[] ParseVersion(string tagName)
        {
            var cleaned = (tagName ?? "").Trim().TrimStart('v', 'V');
            var parts = cleaned.Split('.');
            var result = new int[4];
            for (int i = 0; i < 4 && i < parts.Length; i++)
            {
                int.TryParse(parts[i], out result[i]);
            }
            return result;
        }
    }
}
