using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Text.RegularExpressions;
using System.Linq;
using System;
using System.Xml;

public class NoesisCsBindingsGenerator {
    public class NoesisGeneratedAttribute :Attribute { }
    public static void GenerateCsFile(string filename) {
        var generator = new NoesisCsBindingsGenerator();
        generator.XamlAsset = AssetDatabase.LoadAssetAtPath<NoesisXaml>(filename.Replace(".xaml", ".asset"));
        generator.XamlText = File.ReadAllText(filename);
        generator.SaveFile();
    }

    public NoesisXaml XamlAsset { get; set; }
    public string XamlText { get; set; }

    private static readonly Regex NameRegex = new Regex("x:Name=\"([\\w\\d_]+)\""); // x:Name="MyElementNameHere"
    private static readonly Regex XmlnsRegex = new Regex("xmlns:([\\w\\d_]+)=\"clr-namespace:([^;\"]+)(?:;.*)?\""); // xmlns:designerui="clr-namespace:Assets.UI.Views.DesignerUI"
    private static readonly Regex XamlCodeBehindRegex = new Regex("x:Class=\"(.+)\""); // x:Class="Assets.UI.Views.DesignerUI.CircleButton"
    private const string DummyAttribute = "[UnityEngine.HideInInspector]";

    private void SaveFile() {
        if (HasExistingUserImplemention())
            return;
        var file = GetCsFileName();
        var csText = GetCsText();

        if (csText == null) {
            if (File.Exists(file))
                File.Delete(file);
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<TextAsset>(file) != null) {
            var oldFile = File.ReadAllText(file);
            if (oldFile == csText)
                return;
        }

        File.WriteAllText(file, csText);
        AssetDatabase.ImportAsset(file);
    }

    private string GetCsText() {
        var namespaceString = GetNamespaceString();
        var className = GetClassNameString();
        var inheritsClassName = GetBaseClassName();
        var includedNamespaces = GetXamlIncludedNamespaces().ToArray();
        var events = GetEvents().ToArray();
        var elements = GetNamedElements()
            .Select(p=>new XamlElement {
                Name = p.Name,
                Type = ReplaceXamlNamespace(p.Type, includedNamespaces)
            })
            .ToArray();

        var hasNamedElements = elements.Any();
        var hasEvents = events.Any();
        var hasCodeBehind = className != null && namespaceString != null;

        if (hasNamedElements == false && hasEvents == false && hasCodeBehind == false)
            return null;

        var stringBuilder = new System.Text.StringBuilder();
        stringBuilder.AppendLine("/* This file has been generated automatically. All user changes will be overwritten if the XAML is changed. */");
        stringBuilder.AppendLine("using Noesis;");
        stringBuilder.AppendLine();
        stringBuilder.Append("namespace ").Append(namespaceString).AppendLine(" {");
        stringBuilder.AppendLine("\t" + DummyAttribute);
        stringBuilder.Append("\tpublic partial class ").Append(className).Append(" : ").Append(inheritsClassName).AppendLine(" {");

        {
            if (hasNamedElements)
                stringBuilder.AppendLine();
            foreach (var item in elements)
                stringBuilder
                    .Append("\t\tpublic ")
                    .Append(item.Type)
                    .Append(" ")
                    .Append(item.Name)
                    .AppendLine(";");

            stringBuilder.AppendLine();
            stringBuilder.AppendLine("\t\tprivate void InitializeComponent() {");
            stringBuilder.Append("\t\t\tGUI.LoadComponent(this, \"").Append(XamlAsset.source).AppendLine("\");");

            if(hasNamedElements)
                stringBuilder.AppendLine();
            foreach (var item in elements) {
                stringBuilder
                    .Append("\t\t\tthis.")
                    .Append(item.Name)
                    .Append(" = (")
                    .Append(item.Type)
                    .Append(")FindName(\"")
                    .Append(item.Name)
                    .AppendLine("\");");
            }

            stringBuilder.AppendLine("\t\t}");
        }

        if (hasEvents) {
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("\t\tprotected override bool ConnectEvent(object s, string e, string h) {");
            foreach (var evt in GetEvents()) {
                stringBuilder
                    .Append("\t\t\tif(s is ")
                    .Append(evt.Type)
                    .Append(" && e==\"")
                    .Append(evt.EventName)
                    .Append("\" && h==\"")
                    .Append(evt.HandlerName)
                    .AppendLine("\") {");

                stringBuilder
                    .Append("\t\t\t\t((")
                    .Append(evt.Type)
                    .Append(")s).")
                    .Append(evt.EventName)
                    .Append("+=")
                    .Append(evt.HandlerName)
                    .AppendLine(";");

                stringBuilder.AppendLine("\t\t\t\treturn true;");
                stringBuilder.AppendLine("\t\t\t}");
            }
            stringBuilder.AppendLine("\t\t\treturn false;");
            stringBuilder.AppendLine("\t\t}");
        }

        stringBuilder.AppendLine("\t}");
        stringBuilder.AppendLine("}");

        return stringBuilder.ToString();
    }

    // check if the class already exists and implements the InitializeComponent() and is not a generated class
	private bool HasExistingUserImplemention() {
		var type = GetType(GetNamespaceString() + "." + GetClassNameString());
		if(type == null)
			return false;
		var initializeComponentMethod = type.GetMethod("InitializeComponent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		var dummyAttributes = type.GetCustomAttributes(typeof(HideInInspector), true);
		return initializeComponentMethod != null && dummyAttributes.Any() == false;
	}

    // gets string for the namespace without the class name
    private string GetNamespaceString() {
        var codeBehindClassMatch = XamlCodeBehindRegex.Match(XamlText);
        if (codeBehindClassMatch.Success == false)
            return null;
        var namespaceString = codeBehindClassMatch.Groups[1].Value;
        var lastIndex = namespaceString.LastIndexOf('.');
        namespaceString = namespaceString.Substring(0, lastIndex);
        return namespaceString;
    }

    // get string for class name without the namespace
    private string GetClassNameString() {
        var codeBehindClassMatch = XamlCodeBehindRegex.Match(XamlText);
        if (codeBehindClassMatch.Success == false)
            return null;
        var namespaceString = codeBehindClassMatch.Groups[1].Value;
        var lastIndex = namespaceString.LastIndexOf('.');
        var className = namespaceString.Substring(lastIndex + 1);
        namespaceString = namespaceString.Substring(0, lastIndex);
        return className;
    }

    private string GetBaseClassName() {
        var inheritsClassName = "";
        var startIndex = 0;
        while (XamlText[startIndex] != '<')
            startIndex++;
        startIndex += 1;
        for (var i = startIndex; XamlText[i] != ' ' && XamlText[i] != '\r' && XamlText[i] != '\n'; i++)
            inheritsClassName += XamlText[i];

        return inheritsClassName;
    }

    private struct XamlElement {
        public string Type { get; set; }
        public string Name { get; set; }
    }

    // enumerate x:Name="" definitions
    private IEnumerable<XamlElement> GetNamedElements() {
        var elements = new List<XamlElement>();
        var matches = NameRegex.Matches(XamlText);
        foreach (Match match in matches) {
            // backwards search for owner element start
            var elementStartIndex = -1;
            for (var i = match.Index; XamlText[i] != '<'; i--)
                elementStartIndex = i;

            // forward search for owner element start
            var xamlTypeName = "";
            for (var i = elementStartIndex; XamlText[i] != ' ' && XamlText[i] != '\r' && XamlText[i] != '\n'; i++)
                xamlTypeName += XamlText[i];

            var name = match.Groups[1].Value;
            yield return new XamlElement {
                Type = xamlTypeName,
                Name = name
            };
        }
    }

    private struct XamlNamespaceInclusion {
        public string XamlName { get; set; }
        public string ClrNamespace { get; set; }
    }

    // enumerate xmlns:local="" definitions
    private IEnumerable<XamlNamespaceInclusion> GetXamlIncludedNamespaces() {
        var matches = XmlnsRegex.Matches(XamlText);
        foreach (Match match in matches)
            yield return new XamlNamespaceInclusion {
                XamlName = match.Groups[1].Value,
                ClrNamespace = match.Groups[2].Value
            };
    }

    // replace strings "local:MyCircle" with "MyApp.CustomControls.MyCircle"
    private string ReplaceXamlNamespace(string xamlElementType, IEnumerable<XamlNamespaceInclusion> includedNamespaces) {
        foreach (var ns in includedNamespaces) {
            var before = xamlElementType;
            xamlElementType = xamlElementType.Replace(ns.XamlName + ":", ns.ClrNamespace + ".");
            if (xamlElementType != before)
                break;
        }
        return xamlElementType;
    }

    private string GetCsFileName() {
        return XamlAsset.source.Replace(".xaml", ".g.cs");
    }

    private struct EventConnection {
        public string EventName { get; set; }
        public string HandlerName { get; set; }
        public string Type { get; set; }
    }

    private IEnumerable<EventConnection> GetEvents() {
        var xmlDoc = new XmlDocument();
        xmlDoc.LoadXml(XamlText);

        var includedNamespaces = GetXamlIncludedNamespaces().ToArray();

        foreach (var node in XmlRecursion(xmlDoc).Skip(1)) { // first element is always #document, skip it
            // prevent handling of nested property tags <Grid><Grid.Resources /></Grid>
            if (node.Name.StartsWith(node.ParentNode.Name) && node.Name != node.ParentNode.Name)
                continue;

            if (node.Name.StartsWith("x:"))
                continue;

            var typename = ReplaceXamlNamespace(node.Name, includedNamespaces);
            Type type;
            if(typename == node.Name)
                type = GetType("Noesis."+typename);
            else
                type = GetType(typename);

            if (node.Attributes == null)
                continue;

            foreach (XmlAttribute attribute in node.Attributes) {
                var evt = type.GetEvent(attribute.Name);
                if (evt == null)
                    continue;
                yield return new EventConnection {
                    EventName = attribute.Name,
                    HandlerName = attribute.Value,
                    Type = typename
                };
            }
        }

        yield break;
    }

    private IEnumerable<XmlNode> XmlRecursion(XmlNode node) {
        yield return node;
        var children = node.ChildNodes;
        foreach (XmlNode child in node.ChildNodes) {
            foreach (var child2 in XmlRecursion(child)){
                yield return child2;
            }
        }
    }

    // https://stackoverflow.com/a/11811046
    public static Type GetType(string typeName) {
        var type = Type.GetType(typeName);
        if (type != null) return type;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) {
            type = a.GetType(typeName);
            if (type != null)
                return type;
        }
        return null;
    }

}