using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using net.kvdb.webdav;

namespace Webdav2Csv {
	public class Program {
		public static void Main(string[] args) {
			Debug.Listeners.Add(new ConsoleTraceListener());
			WebDAVClient c = new WebDAVClient();
			c.Server = args[0];

			Queue<string> paths = new Queue<string>();
			paths.Enqueue(args[1]);
			while (paths.Count > 0) {
				var path = paths.Dequeue();
				var task = c.List(path, 9999);
				task.Wait();
				Console.Out.WriteLine(path);
				foreach (var ff in task.Result.files) {
					Console.Out.WriteLine("  " + ff.RelativePath);
					if (ff.IsFolder) {
						paths.Enqueue(path + "/" + ff.RelativePath);
					}
				}
			}
		}
	}
}