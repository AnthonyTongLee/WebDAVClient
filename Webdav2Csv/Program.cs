using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using net.kvdb.webdav;

namespace Webdav2Csv {
	public class Program {
		public static void Main(string[] args) {
			var client = new WebDAVClient(server: args[0]);

			var csvConfig = new CsvConfiguration {Encoding = Encoding.UTF8};

			var tempFile = Path.GetTempFileName();
			try {
				using (var tmpStream = File.Open(tempFile, FileMode.Truncate, FileAccess.ReadWrite, FileShare.None)) {
					// write contents to temporary file and remember the headers that were used
					List<string> headers;
					using (TextWriter textWriter = new StreamWriter(tmpStream, Encoding.UTF8, csvConfig.BufferSize, leaveOpen: true)) {
						using (var csvWriter = new CsvWriter(textWriter, csvConfig)) {
							headers = TraverseAndWrite(client, csvWriter, initialPath: args[1]);
						}
					}

					// write headers to output
					using (var outStream = File.Open("out.csv", FileMode.Create, FileAccess.Write, FileShare.None)) {
						using (TextWriter textWriter = new StreamWriter(outStream, Encoding.UTF8, csvConfig.BufferSize, leaveOpen: true)) {
							using (var csvWriter = new CsvWriter(textWriter, csvConfig)) {
								foreach (var header in headers) {
									csvWriter.WriteField(header);
								}
								csvWriter.NextRecord();
							}
						}

						// copy contents into output
						tmpStream.Seek(0, SeekOrigin.Begin);
						tmpStream.CopyTo(outStream);
					}
				}
			}
			finally {
				File.Delete(tempFile);
			}
		}



		private static List<string> TraverseAndWrite(WebDAVClient client, ICsvWriter csvWriter, string initialPath) {
			var headers = new List<string>();

			// hardcoded href
			headers.Add("href");

			var paths = new Stack<string>();
			paths.Push(initialPath);

			while (paths.Count > 0) {
				var path = paths.Pop();
				var task = client.List(path);
				task.Wait();
				Console.Out.WriteLine(path);

				foreach (var ff in task.Result.files) {
					Console.Out.WriteLine("  " + ff.RelativePath);
					csvWriter.WriteField(ff.Href);

					if (ff.IsFolder) {
						// walk into this folder next (depth-first)
						paths.Push(path + "/" + ff.RelativePath);
					}

					client.Props(ff).Wait();

					// write all known headers, then add any new ones
					var leftovers = ff.PropNameToProp.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
					foreach (var header in headers.ToList()) {
						if (ff.PropNameToProp.ContainsKey(header)) {
							var value = ff.PropNameToProp[header].Value;
							csvWriter.WriteField(value);

							leftovers.Remove(header);
						}
					}
					foreach (var kvp in leftovers) {
						csvWriter.WriteField(kvp.Value.Value);
						headers.Add(kvp.Key);
					}

					csvWriter.NextRecord();
				}
			}

			return headers;
		}
	}
}