using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;

namespace UnityLauncherPro
{
    public static class GetUnityUpdates
    {
        static bool isDownloadingUnityList = false;
        static readonly string unityVersionsURL = @"http://symbolserver.unity3d.com/000Admin/history.txt";

        public static async Task<string> Scan()
        {
            if (isDownloadingUnityList == true)
            {
                Console.WriteLine("We are already downloading ...");
                return null;
            }

            isDownloadingUnityList = true;
            //SetStatus("Downloading list of Unity versions ...");
            string result;
            // download list of Unity versions
            using (WebClient webClient = new WebClient())
            {

                Task<string> downloadStringTask = webClient.DownloadStringTaskAsync(new Uri(unityVersionsURL));
                result = await downloadStringTask;
                isDownloadingUnityList = false;
            }
            return result;
        }

        public static Updates[] Parse(string items)// object sender, DownloadStringCompletedEventArgs e)
        {
            isDownloadingUnityList = false;
            //SetStatus("Downloading list of Unity versions ... done");
            var receivedList = items.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            if (receivedList == null && receivedList.Length < 1) return null;
            Array.Reverse(receivedList);
            var updates = new List<Updates>();
            // parse into data
            for (int i = 0, len = receivedList.Length; i < len; i++)
            {
                var row = receivedList[i].Split(',');
                var versionTemp = row[6].Trim('"');
                var u = new Updates();
                u.ReleaseDate = DateTime.ParseExact(row[3], "MM/dd/yyyy", CultureInfo.InvariantCulture); //DateTime ? lastUpdated = Tools.GetLastModifiedTime(csprojFile);
                u.Version = versionTemp;
                updates.Add(u);
            }

            return updates.ToArray();
        }

    }
}
