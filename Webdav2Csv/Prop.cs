namespace net.kvdb.webdav {
	public class Prop {
		public string Name;
		public string NamespaceUri;
		public string NamespaceAlias;

		public string Value;
		public string RawValue;



		public Prop(string name, string namespaceUri, string namespaceAlias) {
			Name = name;
			NamespaceUri = namespaceUri;
			NamespaceAlias = namespaceAlias;
		}
	}
}