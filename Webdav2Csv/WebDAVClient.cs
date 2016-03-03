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
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using Webdav2Csv;

namespace net.kvdb.webdav {
	/// <remarks>http://webdav.org/specs/rfc4918.html</remarks>
	public class WebDAVClient {
		private readonly string server;
		private string _basePath = "/";



		public WebDAVClient(string server) {
			this.server = server.TrimEnd('/');
		}



		/// <summary>
		/// Specify the path of a WebDAV directory to use as 'root' (default: /)
		/// </summary>
		public string BasePath
		{
			get { return _basePath; }
			set
			{
				value = value.Trim('/');
				_basePath = "/" + value + "/";
			}
		}

		public int? Port { get; set; }

		public string User { get; set; }

		public string Password { get; set; }

		public string Domain { get; set; }



		Uri AsServerUrl(string path, bool appendTrailingSlash) {
			var completePath = _basePath;
			if (path != null) {
				completePath += path.Trim('/');
			}

			if (appendTrailingSlash && completePath.EndsWith("/") == false) {
				completePath += '/';
			}

			if (Port.HasValue) {
				return new Uri(server + ":" + Port + completePath);
			}
			else {
				return new Uri(server + completePath);
			}
		}



		public async Task<ListResult> List(string remoteFilePath = "/") {
			var listUri = AsServerUrl(remoteFilePath, true);

			byte[] content = Encoding.UTF8.GetBytes(
				@"<?xml version=""1.0"" encoding=""utf-8"" ?>
<propfind xmlns=""DAV:"">
	<propname/>
</propfind>
"
				);

			var headers = new Dictionary<string, string> {{"Depth", "1,noroot"}};

			var request = await OpenRequest(listUri, "PROPFIND", headers, content, null);

			var result = new ListResult();
			var response = (HttpWebResponse) await request.GetResponseAsync();
			using (response) {
				result.statusCode = response.StatusCode;
				using (var stream = response.GetResponseStream()) {
					var xml = new XmlDocument();
					xml.Load(stream);

					var normalisedListPath = listUri.ToString().TryRemoveTrailingSlash();

					var xmlNsManager = new XmlNamespaceManager(xml.NameTable);
					xmlNsManager.AddNamespace("d", "DAV:");

					foreach (XmlNode responseNode in xml.DocumentElement.ChildNodes) {
						var ff = new FileFolder();

						var propNode = responseNode.SelectSingleNode("d:propstat/d:prop", xmlNsManager);
						foreach (XmlElement propChildNode in propNode.ChildNodes) {
							var prop = new Prop(propChildNode.Name, propChildNode.NamespaceURI, propChildNode.Prefix);
							ff.PropNameToProp.Add(propChildNode.LocalName, prop);
						}

						var href = responseNode.SelectSingleNode("d:href", xmlNsManager).InnerText;
						ff.Href = HttpUtility.UrlDecode(href);

						var normalisedPath = HttpUtility.UrlDecode(href) ?? string.Empty;
						if (normalisedPath.StartsWith(normalisedListPath, StringComparison.InvariantCultureIgnoreCase)) {
							var relativePath = normalisedPath.Substring(normalisedListPath.Length);
							if (relativePath.StartsWith("/", StringComparison.InvariantCultureIgnoreCase)) {
								relativePath = relativePath.Substring(1, relativePath.Length - 1);
							}
							ff.RelativePath = relativePath;
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



		public async Task<HttpStatusCode> Props(FileFolder fileFolder) {
			byte[] content;
			{
				var s = new StringBuilder();
				s.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");

				s.AppendLine("<D:propfind xmlns:D=\"DAV:\">");
				s.Append("  <D:prop");
				var namespaces = fileFolder.PropNameToProp.Values
					.Where(v => v.NamespaceUri != "DAV:")
					.Select(v => new {v.NamespaceAlias, v.NamespaceUri})
					.Distinct();
				foreach (var ns in namespaces) {
					s.AppendFormat(" xmlns:{0}=\"{1}\"", ns.NamespaceAlias, HttpUtility.HtmlAttributeEncode(ns.NamespaceUri));
				}
				s.AppendLine(">");

				foreach (var prop in fileFolder.PropNameToProp.Values) {
					s.AppendFormat("    <{0} />", HttpUtility.HtmlEncode(prop.Name));
					s.AppendLine();
				}
				s.AppendLine("</D:prop>");
				s.AppendLine("</D:propfind>");
				content = Encoding.UTF8.GetBytes(s.ToString());
			}

			var url = new Uri(fileFolder.Href);
			var headers = new Dictionary<string, string> {{"Depth", "0"}};

			var request = await OpenRequest(url, "PROPFIND", headers, content, null);

			var response = (HttpWebResponse) await request.GetResponseAsync();
			using (response) {
				using (var stream = response.GetResponseStream()) {
					var xml = new XmlDocument();
					xml.Load(stream);

					var xmlNsManager = new XmlNamespaceManager(xml.NameTable);
					xmlNsManager.AddNamespace("d", "DAV:");

					var propNode = xml.DocumentElement.SelectSingleNode("/d:multistatus/d:response/d:propstat/d:prop", xmlNsManager);
					foreach (XmlNode propChildNode in propNode.ChildNodes) {
						var rawValue = propChildNode.InnerXml;
						var value = propChildNode.InnerText;
						var name = propChildNode.LocalName;
						var prop = fileFolder.PropNameToProp[name];
						prop.Value = value;
						prop.RawValue = rawValue;
					}
				}
				return response.StatusCode;
			}
		}



		public async Task<HttpStatusCode> Upload(string localFilePath, string remoteFilePath) {
			var uploadUri = AsServerUrl(remoteFilePath, false);

			var request = await OpenRequest(uploadUri, WebRequestMethods.Http.Put, null, null, localFilePath);

			var response = (HttpWebResponse) await request.GetResponseAsync();
			using (response) {
				return response.StatusCode;
			}
		}



		public async Task<int> Download(string remoteFilePath, string localFilePath) {
			// Should not have a trailing slash.
			var downloadUri = AsServerUrl(remoteFilePath, false);
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



		public async Task<HttpStatusCode> CreateDir(string remotePath) {
			var dirUri = AsServerUrl(remotePath, false);

			var request = await OpenRequest(dirUri, WebRequestMethods.Http.MkCol, null, null, null);
			var response = (HttpWebResponse) await request.GetResponseAsync();
			using (response) {
				return response.StatusCode;
			}
		}



		public async Task<HttpStatusCode> Delete(string remoteFilePath) {
			var delUri = AsServerUrl(remoteFilePath, remoteFilePath.EndsWith("/"));

			var request = await OpenRequest(delUri, "DELETE", null, null, null);

			var response = (HttpWebResponse) await request.GetResponseAsync();
			using (response) {
				return response.StatusCode;
			}
		}



		async Task<HttpWebRequest> OpenRequest(Uri uri, string requestMethod, IDictionary<string, string> headers = null, byte[] content = null, string uploadFilePath = null) {
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
			if (User != null && Password != null) {
				NetworkCredential networkCredential;
				if (Domain != null) {
					networkCredential = new NetworkCredential(User, Password, Domain);
				}
				else {
					networkCredential = new NetworkCredential(User, Password);
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