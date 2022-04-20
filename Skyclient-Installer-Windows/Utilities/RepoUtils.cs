﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Skyclient.JsonParts;

namespace Skyclient.Utilities
{
    public class RepoUtils
    {
        public static string InternalLinkHost = "https://cdn.jsdelivr.net/gh/nacrt/SkyblockClient-REPO@main/files/";
        public static string InternalLinkCdn = "https://raw.githubusercontent.com/nacrt/SkyblockClient-REPO/main/files/";

        public static string SkyclientTempData = "";

        public static string SkyclientDirectory = "";

        public static void Initialize()
        {
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            SkyclientDirectory = Path.Combine(appdata, ".minecraft", "skyclient");
            SkyclientTempData = Path.Combine(appdata, ".skyclient-temp");

            var commitsMain = _DownloadFileString("https://api.github.com/repos/nacrt/SkyblockClient-REPO/commits/main");
            var mainSha = JsonConvert.DeserializeObject<CommitsAPI>(commitsMain);
            Console.WriteLine("Commit SHA: " + mainSha.Sha);
            InternalLinkHost = $"https://cdn.jsdelivr.net/gh/nacrt/SkyblockClient-REPO@{mainSha.Sha}/files/";
        }

        public static JsonSerializerSettings JsonSerializerSettings => new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
            DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate
        };

        public static string GetTempFilePath(DownloadableFile file)
        {
            var filename = Path.GetFileName(file.FileDestination);
            var guidfilename = file.Guid.ToString() + "-" + filename;
            var completepath = Path.Combine(SkyclientTempData, guidfilename);
            return completepath;
        }

        public static void AddRepoItemToQueue(RepoItem item)
        {
            var dest = Path.Combine(SkyclientDirectory, item.LocalFolderName, item.File);
            Repository.Instance.AddToDownloadQueue(new DownloadableFile(dest, item.DownloadLink));

            foreach (var package in item.Packages)
            {
                var packageitem = Repository.Instance.GetModByRepoID(package);
                if (packageitem is null)
                    continue;

                dest = Path.Combine(SkyclientDirectory, packageitem.LocalFolderName, packageitem.File);
                Repository.Instance.AddToDownloadQueue(new DownloadableFile(dest, packageitem.DownloadLink));
            }
        }

        public static string CalculateMD5(string file)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                using (Stream stream = File.OpenRead(file))
                {
                    var hash = md5.ComputeHash(stream); // :trolley:
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        public static void RemoveRepoItem(RepoItem item)
        {
            void individualAction(RepoItem item)
            {
                for (int i = 0; i < item.LocalFiles.Length; i++)
                {
                    var dest = Path.Combine(SkyclientDirectory, item.LocalFolderName, item.LocalFiles[i]);
                    var df = new DownloadableFile(dest, item.DownloadLink);
                    item.LocalFiles = new string[] { item.File };

                    // in case it is queued
                    Repository.Instance.RemoveFromDownloadQueue(df);

                    // TODO: check file hash and termine if it should be sent to temp or removed
                    if (File.Exists(dest))
                    {
                        Console.WriteLine("no file");
                    }

                    if (!item.IsSetHash())
                    {
                        Console.WriteLine("no hash");
                        File.Delete(dest);
                        continue;
                    }

                    var localhash = CalculateMD5(dest);
                    Console.WriteLine(localhash);
                    if (item.Hash == localhash)
                    {
                        Console.WriteLine("same hash");
                        Repository.Instance.RemoveFile(df);
                    }
                    else
                    {
                        Console.WriteLine("different hash");
                        File.Delete(dest);
                    }
                }
            }

            individualAction(item);

            foreach (var package in item.Packages)
            {
                var packageitem = Repository.Instance.GetModByRepoID(package);
                if (packageitem is null)
                    continue;

                for (int i = 0; i < packageitem.LocalFiles.Length; i++)
                {
                    var dest = Path.Combine(SkyclientDirectory, packageitem.LocalFolderName, packageitem.LocalFiles[i]);
                    var df = new DownloadableFile(dest, packageitem.DownloadLink);

                    Repository.Instance.RemoveFromDownloadQueue(df);
                    Repository.Instance.RemoveFile(df);
                }

            }
        }

        // returns full unique file path
        // e.g. C:/[...]/Roaming/.skyclient-temp/-51451681-itlt-1.8.8-9-1.0.1.jar
        // returns null when download was canceled or error
        public static async Task<string?> DownloadTempFile(DownloadableFile file)
        {
            var completepath = GetTempFilePath(file);

            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "curl/7.73.0");
#if DEBUG
            Console.WriteLine("Download link: " + file.FileSource);
#endif
            using (var response = await client.GetStreamAsync(file.FileSource))
            {
                if (file.CancelDownload)
                {
                    PrepareCancelDownload(completepath, file);
                    return null;
                }
                var read = 0;
                //var expected = response.Length;
                var expected = 0;
                var totalread = 0;
                var totalwrote = 0;
                var buffer = new byte[1];
                //var buffer = new byte[4096];
                File.WriteAllBytes(completepath, new byte[0]);
                using (FileStream fs = new FileStream(completepath, FileMode.Append, FileAccess.Write))
                {
                    do
                    {
                        if (file.CancelDownload)
                        {
#if DEBUG
                            Console.Write("expected: " + expected);
                            Console.Write(" - read: " + totalread);
                            Console.WriteLine(" - written: " + totalwrote);
#endif
                            fs.Close();
                            response.Close();

                            PrepareCancelDownload(completepath, file);
                            return null;
                        }

                        read = response.Read(buffer, 0, buffer.Length);
                        totalread += buffer.Length;
                        totalwrote += read;
                        await fs.WriteAsync(buffer, 0, read);

                    } while (read == buffer.Length);
                }
#if DEBUG
                Console.Write("expected: " + expected);
                Console.Write(" - read: " + totalread);
                Console.WriteLine(" - written: " + totalwrote);
#endif

            }
            return completepath;
        }

        private static void PrepareCancelDownload(string completepath, DownloadableFile file)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Canceled download: " + Path.GetFileName(file.FileDestination));
            Console.ResetColor();

            file.CancelDownload = false;
            File.Delete(completepath);
        }

        public static void MoveFile(string filesrc, string filedest)
        {
            var dnsrc = Path.GetDirectoryName(filesrc);
            var dndest = Path.GetDirectoryName(filedest);

            Directory.CreateDirectory(dnsrc);
            Directory.CreateDirectory(dndest);

            try
            {
                File.Move(filesrc, filedest);
            }
            catch (FileNotFoundException fnfe)
            {
                Console.WriteLine("Couldn't find");
                Console.WriteLine("File: " + filesrc);
            } 
            catch (Exception e)
            {
                Console.WriteLine("An unexpected Exception occoured moving");
                Console.WriteLine("File: " + filesrc);
                Console.WriteLine("To: " + filedest);
                Console.WriteLine("Reason: " + e.Message);
            }
        }

        private static string _DownloadFileString(string address)
        {
            using (var wc = new WebClient()) // webclient because it allows syncronous downloads
            {
                wc.Headers.Add(HttpRequestHeader.UserAgent, "curl/7.73.0"); // :weirdchamp:
                return wc.DownloadString(address);
            }
        }

        public static string DownloadRepoFileString(string file)
        {
            return _DownloadFileString(Path.Combine(InternalLinkHost, file));
        }

        public static string GetQualifiedCdn(string pathto)
        {
            return Path.Combine(InternalLinkCdn, pathto);
        }

        public static string GetQualifiedHost(string pathto)
        {
            return Path.Combine(InternalLinkHost, pathto);
        }
    }
}