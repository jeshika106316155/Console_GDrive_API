using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using static System.Drawing.Image;

namespace DriveQuickstart
{
    class Program
    {
        /* Global instance of the scopes required by this quickstart.
         If modifying these scopes, delete your previously saved token.json/ folder. */
        static string[] Scopes = { DriveService.Scope.DriveReadonly, DriveService.Scope.DriveFile };
        static string ApplicationName = "Drive API .NET Quickstart";
        private const string PathToServiceAccountKeyFile = @"D:\\Jeshika\\Other\\Xiao_Laoshi\\ConsoleTestDriveApi\\ConsoleTestDriveApi\\credentials.json";
        public static string UploadFileName = @"D:\Jeshika\Other\Xiao_Laoshi\ConsoleTestDriveApi\ConsoleTestDriveApi\fhir_logo.png"; //"D:\\Jeshika\\Other\\Xiao_Laoshi\\ConsoleTestDriveApi\\ConsoleTestDriveApi\\test_hello.txt";
        public static string mediaJson = @"D:\Jeshika\Other\Xiao_Laoshi\ConsoleTestDriveApi\ConsoleTestDriveApi\FHIR_Media.json";
        public static string fhirUrl = "https://hapi.fhir.org/baseR4/Media/";
        public static string folderName = "SLI_UploadImage";
        static async Task Main(string[] args)
        {
            try
            {

                UserCredential credential;
                // Load client secrets.
                using (var stream =
                       new FileStream(PathToServiceAccountKeyFile, FileMode.Open, FileAccess.Read))
                {
                    /* The file token.json stores the user's access and refresh tokens, and is created
                     automatically when the authorization flow completes for the first time. */
                    string credPath = "token.json";
                    credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.FromStream(stream).Secrets,
                        Scopes,
                        "user",
                        CancellationToken.None,
                        new FileDataStore(credPath, true)).Result;
                    Console.WriteLine("Credential file saved to: " + credPath);
                }

                //GoogleAuthorizationCodeFlow flow = new GoogleAuthorizationCodeFlow.Builder(httpTransport, JSON_FACTORY, clientSecrets,DriveScopes.all()).setDataStoreFactory(dataStoreFactory).build();
                // Create Drive API service.
                var service = new DriveService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName
                });

                // Define parameters of request.
                FilesResource.ListRequest listRequest = service.Files.List();
                listRequest.PageSize = 10;
                listRequest.Fields = "nextPageToken, files(id, name)";

                // List files.
                IList<Google.Apis.Drive.v3.Data.File> files = listRequest.Execute()
                    .Files;
                Console.WriteLine("Files:");
                if (files == null || files.Count == 0)
                {
                    Console.WriteLine("No files found.");
                    return;
                }
                foreach (var file in files)
                {
                    Console.WriteLine("{0} ({1})", file.Name, file.Id);
                }

                string folderId = CreateFolder(service, folderName);

                // Upload file Metadata
                var fileMetadata = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = "Test",
                    Parents = new List <string>() { folderId } //"1trDvdnurc7YkTJc-BqdeIBUMVYAIHhfM"
                };

                string uploadedFileId;
                // Create a new file on Google Drive
                await using (var fsSource = new FileStream(UploadFileName, FileMode.Open, FileAccess.Read))
                {
                    // Create a new file, with metadata and stream.
                    var request = service.Files.Create(fileMetadata, fsSource, GetMimeType(UploadFileName));
                    request.Fields = "*";
                    var results = await request.UploadAsync(CancellationToken.None);

                    if (results.Status == Google.Apis.Upload.UploadStatus.Failed) // == UploadStatus.Failed
                    {
                        Console.WriteLine($"Error uploading file: {results.Exception.Message}");
                    }
                    else
                    {
                        JObject data = JObject.Parse(File.ReadAllText(mediaJson));

                        System.Drawing.Image image = System.Drawing.Image.FromStream(fsSource);
                        
                        var size = image.Height.ToString();
                        data["identifier"][0]["system"] = request.ResponseBody.WebContentLink;
                        data["content"]["url"] = request.ResponseBody.WebViewLink;
                        data["content"]["contentType"] = request.ContentType;
                        data["content"]["title"] = request.ResponseBody.Id;
                        data["content"]["creation"] = request.ResponseBody.CreatedTime;
                        data["identifier"][0]["value"] = request.ResponseBody.Id;
                        data["height"] = image.Height.ToString(); 
                        data["width"] = image.Width.ToString();
                        data["issued"] = DateTime.Now;
                        data["content"]["size"] = fsSource.Length;

                        var requestHttp = (HttpWebRequest)WebRequest.Create(fhirUrl);
                        requestHttp.ContentType = "application/json";
                        requestHttp.Method = "POST";

                        using (var streamWriter = new StreamWriter(requestHttp.GetRequestStream()))
                        {
                            streamWriter.Write(data);
                        }

                        var response = (HttpWebResponse)requestHttp.GetResponse();
                        using (var streamReader = new StreamReader(response.GetResponseStream()))
                        {
                            var result = streamReader.ReadToEnd();
                            JObject resultJson = JObject.Parse(result);
                            var id = resultJson["id"];
                            Console.WriteLine("https://hapi.fhir.org/baseR4/Media/" + id);
                        }
                    }
                    uploadedFileId = request.ResponseBody?.Id;
                }
            }
            catch (FileNotFoundException e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private static string GetMimeType(string fileName)
        {
            string mimeType = "application/unknown";
            string ext = System.IO.Path.GetExtension(fileName).ToLower();
            Microsoft.Win32.RegistryKey regKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(ext);
            if (regKey != null && regKey.GetValue("Content Type") != null)
                mimeType = regKey.GetValue("Content Type").ToString();
            return mimeType;
        }
        public static string CreateFolder(DriveService service, string folderName)
        {
            string folderId = Exists(service, folderName);
            if (folderId != "None")
            { return folderId; }

            var file = new Google.Apis.Drive.v3.Data.File();
            file.Name = folderName;
            file.MimeType = "application/vnd.google-apps.folder";
            var request = service.Files.Create(file);
            request.Fields = "id";
            return request.Execute().Id;
        }
        private static string Exists(DriveService service, string name)
        {
            var listRequest = service.Files.List();
            listRequest.PageSize = 100;
            listRequest.Q = $"mimeType = 'application/vnd.google-apps.folder' and name = '{name}' and 'root' in parents and trashed = false"; 
            listRequest.Fields = "files(id,name)";
            var files = listRequest.Execute().Files;

            foreach (var file in files)
            {
                if (name == file.Name)
                { return file.Id; }
            }
            return "None";
        }
    }
}
