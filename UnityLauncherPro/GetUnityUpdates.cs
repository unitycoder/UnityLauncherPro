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

        //public static async Task<List<Updates>> Scan()
        public static async Task<string> Scan()
        {
            /*
            if (isDownloadingUnityList == true)
            {
                //SetStatus("We are already downloading ...");
                return;
            }*/

            isDownloadingUnityList = true;
            //SetStatus("Downloading list of Unity versions ...");
            string result;
            // download list of Unity versions
            using (WebClient webClient = new WebClient())
            {
                var unityVersionsURL = @"http://symbolserver.unity3d.com/000Admin/history.txt";
                Task<string> downloadStringTask = webClient.DownloadStringTaskAsync(new Uri(unityVersionsURL));
                result = await downloadStringTask;
            }

            return result;
        }

        public static Updates[] Parse(string items)// object sender, DownloadStringCompletedEventArgs e)
        {
            // TODO check for error..
            //SetStatus("Downloading list of Unity versions ... done");
            //isDownloadingUnityList = false;

            // parse to list
            var receivedList = items.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Array.Reverse(receivedList);
            //gridUnityUpdates.Rows.Clear();
            // fill in, TODO: show only top 50 or so

            var updates = new List<Updates>();

            for (int i = 0, len = receivedList.Length; i < len; i++)
            {
                var row = receivedList[i].Split(',');
                var versionTemp = row[6].Trim('"');
                //gridUnityUpdates.Rows.Add(row[3], versionTemp);

                // set color if we already have it installed
                //gridUnityUpdates.Rows[i].Cells[1].Style.ForeColor = unityList.ContainsKey(versionTemp) ? Color.Green : Color.Black;

                var u = new Updates();
                u.ReleaseDate = DateTime.ParseExact(row[3], "MM/dd/yyyy", CultureInfo.InvariantCulture); //DateTime ? lastUpdated = Tools.GetLastModifiedTime(csprojFile);
                u.Version = versionTemp;
                updates.Add(u);
            }

            return updates.ToArray();
        }

    }
}
