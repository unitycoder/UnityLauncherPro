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
            string result = null;
            // download list of Unity versions
            using (WebClient webClient = new WebClient())
            {
                Task<string> downloadStringTask = webClient.DownloadStringTaskAsync(new Uri(unityVersionsURL));
                try
                {
                    result = await downloadStringTask;
                }
                catch (WebException)
                {
                    Console.WriteLine("It's a web exception");
                }
                catch (Exception)
                {
                    Console.WriteLine("It's not a web exception");
                }

                isDownloadingUnityList = false;
            }
            return result;
        }

        public static Updates[] Parse(string items, ref List<string> updatesAsString)
        {
            if (updatesAsString == null)
                updatesAsString = new List<string>();
            updatesAsString.Clear();

            isDownloadingUnityList = false;
            //SetStatus("Downloading list of Unity versions ... done");
            var receivedList = items.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            if (receivedList == null && receivedList.Length < 1) return null;
            Array.Reverse(receivedList);
            var releases = new Dictionary<string, Updates>();
            // parse into data
            string prevVersion = null;
            for (int i = 0, len = receivedList.Length; i < len; i++)
            {
                var row = receivedList[i].Split(',');
                var versionTemp = row[6].Trim('"');

                if (versionTemp.Length < 1) continue;
                if (prevVersion == versionTemp) continue;

                if (releases.ContainsKey(versionTemp) == false)
                {
                    var u = new Updates();
                    u.ReleaseDate = DateTime.ParseExact(row[3], "MM/dd/yyyy", CultureInfo.InvariantCulture);
                    u.Version = versionTemp;
                    releases.Add(versionTemp, u);
                    updatesAsString.Add(versionTemp);
                }

                prevVersion = versionTemp;
            }

            // convert to array
            var results = new Updates[releases.Count];
            releases.Values.CopyTo(results, 0);
            return results;
        }

    }
}
