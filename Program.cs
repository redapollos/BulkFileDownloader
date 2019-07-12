using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.FileExtensions;
using Microsoft.Extensions.Configuration.Json;

namespace BulkFileDownloader
{
    class Program
    {
        private static IConfiguration config;

        static void Main(string[] args)
        {
            /*var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json");*/

            config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .Build();

            if(string.IsNullOrEmpty(config["inputFile"]))
            {
                Console.WriteLine("Please specify an input file.");
                return;
            }

            if (string.IsNullOrEmpty(config["outputDirectory"]))
            {
                Console.WriteLine("Please specify an output directory.");
                return;
            }

            Console.WriteLine($"Scanning: {config["inputFile"]}");
            Console.WriteLine($"Loading downloads to: {config["outputDirectory"]}");

            ServicePointManager.DefaultConnectionLimit = 10000;

            // make sure the output dir exists
            if (!Directory.Exists(config["outputDirectory"]))
                Directory.CreateDirectory(config["outputDirectory"]);

            // get list of urls from the input file
            List<string> urls = File.ReadAllLines(config["inputFile"]).ToList();
            
            // multi-threading!
            int retries = urls.AsParallel().WithDegreeOfParallelism(4).Sum(arg => downloadFile(arg));

            Console.WriteLine("Done!");
        }

        static int downloadFile(string url)
        {
            int retries = 0;

            retry:
            try
            {
                // setup the directory
                var invalids = Path.GetInvalidFileNameChars();

                var tempPath = url.Substring(url.IndexOf("//") + 2);
                var relativeFilePath = tempPath.Substring(tempPath.IndexOf("/") + 1);
                var fileName = relativeFilePath.LastIndexOf('/') > -1 ? relativeFilePath.Substring(relativeFilePath.LastIndexOf('/') + 1) : relativeFilePath;
                fileName = string.Join("_", fileName.Split(invalids, StringSplitOptions.RemoveEmptyEntries)); // sanitize the filename for the OS
                var fullDirectory = Path.Combine(config["outputDirectory"], relativeFilePath.Substring(0, relativeFilePath.LastIndexOf('/')));
                var fullFilePath = Path.Combine(fullDirectory, fileName);

                if (!Directory.Exists(fullDirectory))
                    Directory.CreateDirectory(fullDirectory);

                // if the file already exists, then just skip
                if (File.Exists(fullFilePath))
                    return retries;
                
                // get the file
                HttpWebRequest webrequest = (HttpWebRequest)WebRequest.Create(url.Replace(" ", "%20"));
                webrequest.Timeout = 10000;
                webrequest.ReadWriteTimeout = 10000;
                webrequest.Proxy = null;
                webrequest.AllowAutoRedirect = true;
                webrequest.KeepAlive = false;

                // get stream
                var webresponse = (HttpWebResponse)webrequest.GetResponse();
                
                // save file to disk
                using (Stream sr = webresponse.GetResponseStream())
                    using (FileStream sw = File.Create(fullFilePath))
                    {
                        sr.CopyTo(sw);
                    }
            }

            catch (Exception ee)
            {
                if (ee.Message != "The remote server returned an error: (404) Not Found." && 
                    ee.Message != "The remote server returned an error: (403) Forbidden.")
                {
                    if (ee.Message.StartsWith("The operation has timed out") ||
                        ee.Message == "Unable to connect to the remote server" ||
                        ee.Message.StartsWith("The request was aborted: ") ||
                        ee.Message.StartsWith("Unable to read data from the trans­port con­nec­tion: ") ||
                        ee.Message == "The remote server returned an error: (408) Request Timeout.")
                    {
                        if(retries++ < 5)
                           goto retry;
                        else
                            Console.WriteLine($"FAIL: {url} : {ee.Message}");
                    }
                    else
                    {
                        Console.WriteLine($"FAIL: {url} : {ee.Message}");
                    }
                }
            }

            return retries;
        }
    }
}
