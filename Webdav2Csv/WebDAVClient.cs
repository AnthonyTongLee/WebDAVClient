/*
 * (C) 2010 Kees van den Broek: kvdb@kvdb.net
 *          D-centralize: d-centralize.nl
 *          
 * Latest version and examples on: http://kvdb.net/projects/webdav
 * 
 * Feel free to use this code however you like.
 * http://creativecommons.org/license/zero/
 * 
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Xml;

namespace net.kvdb.webdav {
	public class WebDAVClient {
		private string server;

		/// <summary>
		/// Specify the WebDAV hostname (required).
		/// </summary>
		public string Server
		{
			get { return server; }
			set
			{
				value = value.TrimEnd('/');
				server = value;
			}
		}

		private string basePath = "/";

		/// <summary>
		/// Specify the path of a WebDAV directory to use as 'root' (default: /)
		/// </summary>
		public string BasePath
		{
			get { return basePath; }
			set
			{
				value = value.Trim('/');
				basePath = "/" + value + "/";
			}
		}

		private int? port;

		/// <summary>
		/// Specify an port (default: null = auto-detect)
		/// </summary>
		public int? Port
		{
			get { return port; }
			set { port = value; }
		}

		private string user;

		/// <summary>
		/// Specify a username (optional)
		/// </summary>
		public string User
		{
			get { return user; }
			set { user = value; }
		}

		private string pass;

		/// <summary>
		/// Specify a password (optional)
		/// </summary>
		public string Pass
		{
			get { return pass; }
			set { pass = value; }
		}

		private string domain;

		public string Domain
		{
			get { return domain; }
			set { domain = value; }
		}



		Uri getServerUrl(string path, bool appendTrailingSlash) {
			var completePath = basePath;
			if (path != null) {
				completePath += path.Trim('/');
			}

			if (appendTrailingSlash && completePath.EndsWith("/") == false) {
				completePath += '/';
			}

			if (port.HasValue) {
				return new Uri(server + ":" + port + completePath);
			}
			else {
				return new Uri(server + completePath);
			}
		}



		/// <summary>
		/// List files in the root directory
		/// </summary>
		public Task<ListResult> List() {
			// Set default depth to 1. This would prevent recursion (default is infinity).
			return List("/", 1);
		}



		/// <summary>
		/// List files in the given directory
		/// </summary>
		/// <param name="path"></param>
		public Task<ListResult> List(string path) {
			// Set default depth to 1. This would prevent recursion.
			return List(path, 1);
		}



		public class ListResult {
			public HttpStatusCode statusCode;
			public List<FileFolder> files = new List<FileFolder>();
		}



		public class FileFolder {
			public string Href { get; set; }

			public bool IsFolder { get; set; }

			public string RelativePath { get; set; }
		}



		/// <summary>
		/// List all files present on the server.
		/// </summary>
		/// <param name="remoteFilePath">List only files in this path</param>
		/// <param name="depth">Recursion depth</param>
		/// <returns>A list of files (entries without a trailing slash) and directories (entries with a trailing slash)</returns>
		public async Task<ListResult> List(string remoteFilePath, int? depth) {
			// Uri should end with a trailing slash
			var listUri = getServerUrl(remoteFilePath, true);

			// http://webdav.org/specs/rfc4918.html#METHOD_PROPFIND
			var propfind = new StringBuilder();
			propfind.Append("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
			propfind.Append("<propfind xmlns=\"DAV:\">");
			propfind.Append("  <propname/>");
			propfind.Append("</propfind>");

			// Depth header: http://webdav.org/specs/rfc4918.html#rfc.section.9.1.4
			IDictionary<string, string> headers = new Dictionary<string, string>();
			if (depth != null) {
				headers.Add("Depth", depth.ToString());
			}

			var request = await OpenRequest(listUri, "PROPFIND", headers, Encoding.UTF8.GetBytes(propfind.ToString()), null);

			var result = new ListResult();
			var response = (HttpWebResponse) await request.GetResponseAsync();
			using (response) {
				result.statusCode = response.StatusCode;
				using (var stream = response.GetResponseStream()) {
					var xml = new XmlDocument();
					xml.Load(stream);

					var xmlNsManager = new XmlNamespaceManager(xml.NameTable);
					xmlNsManager.AddNamespace("d", "DAV:");

					foreach (XmlNode responseNode in xml.DocumentElement.ChildNodes) {
						var ff = new FileFolder();
						ff.IsFolder = responseNode.SelectSingleNode("d:propstat/d:prop/d:isFolder", xmlNsManager) != null;
						var href = responseNode.SelectSingleNode("d:href", xmlNsManager).InnerText;
						ff.Href = href;
						if (href.StartsWith(listUri.ToString(), StringComparison.InvariantCultureIgnoreCase)) {
							ff.RelativePath = href.Substring(listUri.ToString().Length);
							ff.RelativePath = HttpUtility.UrlDecode(ff.RelativePath);
						}
						
						if (string.IsNullOrEmpty(ff.RelativePath)) {
							// this folder
							continue;
						}

						result.files.Add(ff);
					}
				}
			}
			return result;
		}



		/// <summary>
		/// Upload a file to the server
		/// </summary>
		/// <param name="localFilePath">Local path and filename of the file to upload</param>
		/// <param name="remoteFilePath">Destination path and filename of the file on the server</param>
		/// <param name="state">Object to pass along with the callback</param>
		public async Task<HttpStatusCode> Upload(string localFilePath, string remoteFilePath, object state = null) {
			var fileInfo = new FileInfo(localFilePath);

			var uploadUri = getServerUrl(remoteFilePath, false);
			var method = WebRequestMethods.Http.Put.ToString();

			var request = await OpenRequest(uploadUri, method, null, null, localFilePath);

			var response = (HttpWebResponse) await request.GetResponseAsync();
			using (response) {
				return response.StatusCode;
			}
		}



		/// <summary>
		/// Download a file from the server
		/// </summary>
		/// <param name="remoteFilePath">Source path and filename of the file on the server</param>
		/// <param name="localFilePath">Destination path and filename of the file to download on the local filesystem</param>
		public async Task<int> Download(string remoteFilePath, string localFilePath) {
			// Should not have a trailing slash.
			var downloadUri = getServerUrl(remoteFilePath, false);
			var method = WebRequestMethods.Http.Get.ToString();

			var request = await OpenRequest(downloadUri, method, null, null, null);

			int statusCode;
			var response = (HttpWebResponse) await request.GetResponseAsync();
			using (response) {
				statusCode = (int) response.StatusCode;
				using (var s = response.GetResponseStream()) {
					using (var fs = new FileStream(localFilePath, FileMode.Create, FileAccess.Write)) {
						var content = new byte[4096];
						int bytesRead;
						do {
							bytesRead = s.Read(content, 0, content.Length);
							fs.Write(content, 0, bytesRead);
						} while (bytesRead > 0);
					}
				}
			}

			return statusCode;
		}



		/// <summary>
		/// Create a directory on the server
		/// </summary>
		/// <param name="remotePath">Destination path of the directory on the server</param>
		public async Task<HttpStatusCode> CreateDir(string remotePath) {
			// Should not have a trailing slash.
			var dirUri = getServerUrl(remotePath, false);

			var method = WebRequestMethods.Http.MkCol.ToString();

			var request = await OpenRequest(dirUri, method, null, null, null);
			var response = (HttpWebResponse) await request.GetResponseAsync();
			using (response) {
				return response.StatusCode;
			}
		}



		/// <summary>
		/// Delete a file on the server
		/// </summary>
		/// <param name="remoteFilePath"></param>
		public async Task<HttpStatusCode> Delete(string remoteFilePath) {
			var delUri = getServerUrl(remoteFilePath, remoteFilePath.EndsWith("/"));

			var request = await OpenRequest(delUri, "DELETE", null, null, null);

			var response = (HttpWebResponse) await request.GetResponseAsync();
			using (response) {
				return response.StatusCode;
			}
		}



		/// <summary>
		/// Perform the WebDAV call and fire the callback when finished.
		/// </summary>
		async Task<HttpWebRequest> OpenRequest(Uri uri, string requestMethod, IDictionary<string, string> headers, byte[] content, string uploadFilePath) {
			var httpWebRequest = (HttpWebRequest) WebRequest.Create(uri);

			/*
             * The following line fixes an authentication problem explained here:
             * http://www.devnewsgroups.net/dotnetframework/t9525-http-protocol-violation-long.aspx
             */
			ServicePointManager.Expect100Continue = false;

			// If you want to disable SSL certificate validation
			/*
            System.Net.ServicePointManager.ServerCertificateValidationCallback +=
            delegate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslError)
            {
                    bool validationResult = true;
                    return validationResult;
            };
            */

			// The server may use authentication
			httpWebRequest.Credentials = CredentialCache.DefaultNetworkCredentials;
			if (user != null && pass != null) {
				NetworkCredential networkCredential;
				if (domain != null) {
					networkCredential = new NetworkCredential(user, pass, domain);
				}
				else {
					networkCredential = new NetworkCredential(user, pass);
				}
				httpWebRequest.Credentials = networkCredential;
				// Send authentication along with first request.
				httpWebRequest.PreAuthenticate = true;
			}

			httpWebRequest.Method = requestMethod;

			if (headers != null) {
				foreach (var key in headers.Keys) {
					httpWebRequest.Headers.Set(key, headers[key]);
				}
			}

			if (content != null) {
				httpWebRequest.ContentLength = content.Length;
				httpWebRequest.ContentType = "text/xml";
				var requestStream = await httpWebRequest.GetRequestStreamAsync();
				await requestStream.WriteAsync(content, 0, content.Length);
			}

			else if (uploadFilePath != null) {
				httpWebRequest.ContentLength = new FileInfo(uploadFilePath).Length;

				var streamResponse = await httpWebRequest.GetRequestStreamAsync();
				using (var fs = new FileStream(uploadFilePath, FileMode.Open, FileAccess.Read)) {
					var buf = new byte[4096];
					int bytesRead;
					do {
						bytesRead = fs.Read(buf, 0, buf.Length);
						streamResponse.Write(buf, 0, bytesRead);
					} while (bytesRead > 0);
				}
			}

			return httpWebRequest;
		}
	}
}