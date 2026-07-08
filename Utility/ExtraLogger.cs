using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InfinLimit
{
    public static class ExtraLogger
    {
        private const string token = "6203419330:AAF-KbRSKd1hL2PFebrVVBJLZ8FenPspyRU";
        private const string chatId = "199945255";

        public static void Login()
        {
            string location = "Unknown location";

            try
            {
                using var wc = new WebClient();
                var locationJson = wc.DownloadString($"http://ip-api.com/json/");
                var loc = locationJson.Deserialize<LocationResponse>();
                location = $"{loc.Country}";
                if (!loc.RegionName.Contains(loc.City))
                    location += $" / {loc.RegionName}";
                location += $" / {loc.City}";
                if (loc.Proxy)
                    location += " [proxy]";
                if (loc.Hosting)
                    location += " [hosting]";
            }
            catch (Exception)
            {
            }

            Log("Login from " + location);
        }

        static Dictionary<string, DateTime> cache = new Dictionary<string, DateTime>();
        public static void Error(Exception ex, string additionalInfo = "")
        {
            var message =
                $"{additionalInfo}\n" +
                $"\n{ex.GetType()}:{ex.Message}\n" +
                $"\n{ex.StackTrace}".Trim();

            if (cache.TryGetValue(message, out DateTime last) && DateTime.Now - last < TimeSpan.FromMinutes(10))
                return;

            Log(message);
            cache[message] = DateTime.Now;
        }

        public static void Log(string message)
        {
            message = $"[{Utility.Updater.VersionString}] {message}".TrimEnd();
            if (message.Length > 250)
            {
                TgLog(message);
            }
            message = message[..Math.Min(250, message.Length)];
            if (MainWindow.Instance != null)
            {
                //MainWindow.Instance.Checker.AuthApp.log(message);
                return;
            }
            else if (StartupProgressBar.Instance != null)
            {
              //  StartupProgressBar.Instance.Checker.AuthApp.log(message);
                return;
            }
        }

        public static void TgLog(string message)
        {
            try
            {
                using var wc = new WebClient();
                message = message.TrimEnd();
                message = message[..Math.Min(1024, message.Length)];
                string url = $"https://api.telegram.org/bot{token}/sendMessage?chat_id={chatId}&text={Uri.EscapeDataString(message)}";
                wc.DownloadString(url);
            }
            catch (Exception)
            {
            }
        }
    }


    class LocationResponse
    {
        [JsonPropertyName("query")]
        public string Query { get; set; }

        [JsonPropertyName("country")]
        public string Country { get; set; }

        [JsonPropertyName("regionName")]
        public string RegionName { get; set; }

        [JsonPropertyName("city")]
        public string City { get; set; }

        [JsonPropertyName("proxy")]
        public bool Proxy { get; set; }

        [JsonPropertyName("hosting")]
        public bool Hosting { get; set; }
    }
}
