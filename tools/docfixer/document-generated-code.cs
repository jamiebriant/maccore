//
// This program takes the API definition from the build and
// uses it to generate the documentation for the auto-generated
// code
//
//
//

using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Xml.Linq;
using System.Linq;
using System.Xml.XPath;
using System.Xml;
using System.Text;
#if MONOMAC
using MonoMac.Foundation;
#else
using MonoTouch.Foundation;
#endif

class DocumentGeneratedCode {
#if MONOMAC
	static string ns = "MonoMac";
	Type nso = typeof (MonoMac.Foundation.NSObject);
#else
	static string ns = "MonoTouch";
	Type nso = typeof (MonoTouch.Foundation.NSObject);
#endif

	static void Help ()
	{
		Console.WriteLine ("Usage is: document-generated-code [--appledocs] temp.dll path-to-documentation");
	}

	static string assembly_dir;
	static Assembly assembly;
	static bool mergeAppledocs;
	
	static string GetMdocPath (Type t)
	{
		return string.Format ("{0}/{1}/{2}.xml", assembly_dir, t.Namespace, t.Name);
	}
	
	static Dictionary<Type,XDocument> docs = new Dictionary<Type,XDocument> ();
	static XDocument GetDoc (Type t)
	{
		if (docs.ContainsKey (t))
			return docs [t];
		
		string xmldocpath = GetMdocPath (t);
		if (!File.Exists (xmldocpath)) {
			Console.WriteLine ("DOC REGEN PENDING for type: {0}", t.FullName);
			return null;
		}
		
		XDocument xmldoc;
		try {
			using (var f = File.OpenText (xmldocpath))
				xmldoc = XDocument.Load (f);
			docs [t] = xmldoc;
		} catch {
			Console.WriteLine ("Failure while loading {0}", xmldocpath);
			return null;
		}

		return xmldoc;
	}
	
	static void SaveDocs ()
	{
		foreach (var t in docs.Keys){
			var xmldocpath = GetMdocPath (t);
			var xmldoc = docs [t];
				
			var xmlSettings = new XmlWriterSettings (){
				Indent = true,
				Encoding = new UTF8Encoding (false),
				OmitXmlDeclaration = true
			};
			using (var output = File.CreateText (xmldocpath)){
				var xmlw = XmlWriter.Create (output, xmlSettings);
				xmldoc.Save (xmlw);
				output.WriteLine ();
			}
		}
	}

	//
	// Handles fields, but perhaps this is better done in DocFixer to pull the definitions
	// from the docs?
	//
	public static void ProcessField (Type t, XDocument xdoc, PropertyInfo pi)
	{
		var fieldAttr = pi.GetCustomAttributes (typeof (FieldAttribute), true);
		if (fieldAttr.Length == 0)
			return;
		
		var export = ((FieldAttribute) fieldAttr [0]).SymbolName;
		
		var field = xdoc.XPathSelectElement ("Type/Members/Member[@MemberName='" + pi.Name + "']");
		if (field == null){
			Console.WriteLine ("Warning: {0} document is not up-to-date with the latest assembly", t);
			return;
		}
		var returnType = field.XPathSelectElement ("ReturnValue/ReturnType");
		var summary = field.XPathSelectElement ("Docs/summary");
		var remarks = field.XPathSelectElement ("Docs/remarks");

		if (mergeAppledocs){
			if (returnType.Value == "MonoMac.Foundation.NSString" && export.EndsWith ("Notification")){
				var mdoc = DocGenerator.GetAppleMemberDocs (t, export);
				if (mdoc == null){
					Console.WriteLine ("Failed to load docs for {0} - {1}", t.Name, export);
					return;
				}

				var section = DocGenerator.ExtractSection (mdoc);

				//
				// Make this pretty, the first paragraph we turn into the summary,
				// the rest we put in the remarks section
				//
				summary.Value = "";
				summary.Add (section);

				var skipOne = summary.Nodes ().Skip (2).ToArray ();
				remarks.Value = "";
				remarks.Add (skipOne);
				foreach (var n in skipOne)
					n.Remove ();
			}
		}
	}

	public static void PopulateEvents (XDocument xmldoc, BaseTypeAttribute bta, Type t)
	{
		for (int i = 0; i < bta.Events.Length; i++){
			var delType = bta.Events [i];
			var evtName = bta.Delegates [i];
			foreach (var mi in delType.GatherMethods ()){
				var method = xmldoc.XPathSelectElement ("Type/Members/Member[@MemberName='" + mi.Name + "']");
				if (method == null){
					Console.WriteLine ("Documentation not up to date for {0}, member {1} was not found", delType, mi.Name);
					continue;
				}
				var summary = method.XPathSelectElement ("Docs/summary");
				var remarks = method.XPathSelectElement ("Docs/remarks");
				var returnType = method.XPathSelectElement ("ReturnValue/ReturnType");

				if (mi.ReturnType == typeof (void)){
					summary.Value = "Event raised by the object.";
					remarks.Value = "If you assign a value to this event, this will reset the value for the " + evtName + " property to an internal handler that maps delegates to events.";
				} else {
					summary.Value = "Delegate invoked by the object to get a value.";
					remarks.Value = "You assign a function, delegate or anonymous method to this property to return a value to the object.   If you assign a value to this property, it this will reset the value for the " + evtName + " property to an internal handler that maps delegates to events.";
				}
			}
		}
	}
	
	public static void ProcessNSO (Type t, BaseTypeAttribute bta)
	{
		var xmldoc = GetDoc (t);
		if (xmldoc == null)
			return;
		
		foreach (var pi in t.GatherProperties ()){
			if (pi.GetCustomAttributes (typeof (FieldAttribute), true).Length > 0){
				ProcessField (t, xmldoc, pi);
				continue;
			}
		}

		if (bta.Events != null){
			PopulateEvents (xmldoc, bta, t);
		}
	}
			
	public static int Main (string [] args)
	{
		string dir = null;
		string lib = null;
		var debug = Environment.GetEnvironmentVariable ("DOCFIXER");

		DocGenerator.DebugDocs = false;
		
		for (int i = 0; i < args.Length; i++){
			var arg = args [i];
			if (arg == "-h" || arg == "--help"){
				Help ();
				return 0;
			}
			if (arg == "--appledocs"){
				mergeAppledocs = true;
				continue;
			}
			
			if (lib == null)
				lib = arg;
			else
				dir = arg;
		}
		
		if (dir == null){
			Help ();
			return 1;
		}
		
		if (File.Exists (Path.Combine (dir, "en"))){
			Console.WriteLine ("The directory does not seem to be the root for documentation (missing `en' directory)");
			return 1;
		}
		assembly_dir = Path.Combine (dir, "en");
		assembly = Assembly.LoadFrom (lib);

		foreach (Type t in assembly.GetTypes ()){
			if (debug != null && t.FullName != debug)
				continue;

			var btas = t.GetCustomAttributes (typeof (BaseTypeAttribute), true);
			if (btas.Length > 0)
				ProcessNSO (t, (BaseTypeAttribute) btas [0]);
		}

		Console.WriteLine ("saving");
		SaveDocs ();
		
		return 0;
	}
}