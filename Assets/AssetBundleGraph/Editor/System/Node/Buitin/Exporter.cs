using UnityEngine;
using UnityEditor;

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using AssetBundleGraph.V2;

namespace AssetBundleGraph.V2 {

	[CustomNode("Exporter", 80)]
	public class Exporter : INode {

		[SerializeField] private SerializableMultiTargetString m_exportPath;
		[SerializeField] private SerializableMultiTargetInt m_exportOption;

		public string ActiveStyle {
			get {
				return string.Empty;
			}
		}

		public string InactiveStyle {
			get {
				return string.Empty;
			}
		}

		public void Initialize(NodeData data) {
		}

		public INode Clone() {
			return null;
		}

		public bool Validate(List<NodeData> allNodes, List<ConnectionData> allConnections) {
			return false;
		}

		public bool IsEqual(INode node) {
			return false;
		}

		public string Serialize() {
			return string.Empty;
		}

		public bool IsValidInputConnectionPoint(ConnectionPointData point) {
			return false;
		}

		public bool CanConnectFrom(INode fromNode) {
			return false;
		}

		public bool OnAssetsReimported(BuildTarget target, 
			string[] importedAssets, 
			string[] deletedAssets, 
			string[] movedAssets, 
			string[] movedFromAssetPaths)
		{
			return false;
		}

		public void OnNodeGUI(NodeGUI node) {
		}

		public void OnInspectorGUI (NodeGUI node, NodeGUIEditor editor) {
			
			if (m_exportPath == null) {
				return;
			}

			var currentEditingGroup = editor.CurrentEditingGroup;

			EditorGUILayout.HelpBox("Exporter: Export given files to output directory.", MessageType.Info);
			editor.UpdateNodeName(node);

			GUILayout.Space(10f);

			//Show target configuration tab
			editor.DrawPlatformSelector(node);
			using (new EditorGUILayout.VerticalScope(GUI.skin.box)) {
				var disabledScope = editor.DrawOverrideTargetToggle(node, m_exportPath.ContainsValueOf(currentEditingGroup), (bool enabled) => {
					using(new RecordUndoScope("Remove Target Export Settings", node, true)){
						if(enabled) {
							m_exportPath[currentEditingGroup] = m_exportPath.DefaultValue;
						}  else {
							m_exportPath.Remove(currentEditingGroup);
						}
					}
				} );

				using (disabledScope) {
					ExporterExportOption opt = (ExporterExportOption)m_exportOption[currentEditingGroup];
					var newOption = (ExporterExportOption)EditorGUILayout.EnumPopup("Export Option", opt);
					if(newOption != opt) {
						using(new RecordUndoScope("Change Export Option", node, true)){
							m_exportOption[currentEditingGroup] = (int)newOption;
						}
					}

					EditorGUILayout.LabelField("Export Path:");
					var newExportPath = EditorGUILayout.TextField(
						SystemDataUtility.GetProjectName(), 
						m_exportPath[currentEditingGroup]
					);

					var exporterNodePath = FileUtility.GetPathWithProjectPath(newExportPath);
					if(ValidateExportPath(
						newExportPath,
						exporterNodePath,
						() => {
						},
						() => {
							using (new EditorGUILayout.HorizontalScope()) {
								EditorGUILayout.LabelField(exporterNodePath + " does not exist.");
								if(GUILayout.Button("Create directory")) {
									Directory.CreateDirectory(exporterNodePath);
								}
							}
							EditorGUILayout.Space();

							EditorGUILayout.LabelField("Available Directories:");
							string[] dirs = Directory.GetDirectories(Path.GetDirectoryName(exporterNodePath));
							foreach(string s in dirs) {
								EditorGUILayout.LabelField(s);
							}
						}
					)) {
						using (new EditorGUILayout.HorizontalScope()) {
							GUILayout.FlexibleSpace();
							#if UNITY_EDITOR_OSX
							string buttonName = "Reveal in Finder";
							#else
							string buttonName = "Show in Explorer";
							#endif 
							if(GUILayout.Button(buttonName)) {
								EditorUtility.RevealInFinder(exporterNodePath);
							}
						}
					}

					if (newExportPath != m_exportPath[currentEditingGroup]) {
						using(new RecordUndoScope("Change Export Path", node, true)){
							m_exportPath[currentEditingGroup] = newExportPath;
						}
					}
				}
			}
		}

		public void Prepare (BuildTarget target, 
			NodeData node, 
			IEnumerable<PerformGraph.AssetGroups> incoming, 
			IEnumerable<ConnectionData> connectionsToOutput, 
			PerformGraph.Output Output) 
		{
			ValidateExportPath(
				m_exportPath[target],
				FileUtility.GetPathWithProjectPath(m_exportPath[target]),
				() => {
					throw new NodeException(node.Name + ":Export Path is empty.", node.Id);
				},
				() => {
					if( m_exportOption[target] == (int)ExporterExportOption.ErrorIfNoExportDirectoryFound ) {
						throw new NodeException(node.Name + ":Directory set to Export Path does not exist. Path:" + m_exportPath[target], node.Id);
					}
				}
			);
		}
		
		public void Build (BuildTarget target, 
			NodeData node, 
			IEnumerable<PerformGraph.AssetGroups> incoming, 
			IEnumerable<ConnectionData> connectionsToOutput, 
			PerformGraph.Output Output,
			Action<NodeData, string, float> progressFunc) 
		{
			Export(target, node, incoming, connectionsToOutput, progressFunc);
		}

		private void Export (BuildTarget target, 
			NodeData node, 
			IEnumerable<PerformGraph.AssetGroups> incoming, 
			IEnumerable<ConnectionData> connectionsToOutput, 
			Action<NodeData, string, float> progressFunc) 
		{
			if(incoming == null) {
				return;
			}

			var exportPath = FileUtility.GetPathWithProjectPath(m_exportPath[target]);

			if(m_exportOption[target] == (int)ExporterExportOption.DeleteAndRecreateExportDirectory) {
				if (Directory.Exists(exportPath)) {
					Directory.Delete(exportPath, true);
				}
			}

			if(m_exportOption[target] != (int)ExporterExportOption.ErrorIfNoExportDirectoryFound) {
				if (!Directory.Exists(exportPath)) {
					Directory.CreateDirectory(exportPath);
				}
			}

			var report = new ExportReport(node);

			foreach(var ag in incoming) {
				foreach (var groupKey in ag.assetGroups.Keys) {
					var inputSources = ag.assetGroups[groupKey];

					foreach (var source in inputSources) {					
						var destinationSourcePath = source.importFrom;

						// in bundleBulider, use platform-package folder for export destination.
						if (destinationSourcePath.StartsWith(AssetBundleGraphSettings.BUNDLEBUILDER_CACHE_PLACE)) {
							var depth = AssetBundleGraphSettings.BUNDLEBUILDER_CACHE_PLACE.Split(AssetBundleGraphSettings.UNITY_FOLDER_SEPARATOR).Length + 1;

							var splitted = destinationSourcePath.Split(AssetBundleGraphSettings.UNITY_FOLDER_SEPARATOR);
							var reducedArray = new string[splitted.Length - depth];

							Array.Copy(splitted, depth, reducedArray, 0, reducedArray.Length);
							var fromDepthToEnd = string.Join(AssetBundleGraphSettings.UNITY_FOLDER_SEPARATOR.ToString(), reducedArray);

							destinationSourcePath = fromDepthToEnd;
						}

						var destination = FileUtility.PathCombine(exportPath, destinationSourcePath);

						var parentDir = Directory.GetParent(destination).ToString();

						if (!Directory.Exists(parentDir)) {
							Directory.CreateDirectory(parentDir);
						}
						if (File.Exists(destination)) {
							File.Delete(destination);
						}
						if (string.IsNullOrEmpty(source.importFrom)) {
							report.AddErrorEntry(source.absolutePath, destination, "Source Asset import path is empty; given asset is not imported by Unity.");
							continue;
						}
						try {
							if(progressFunc != null) progressFunc(node, string.Format("Copying {0}", source.fileNameAndExtension), 0.5f);
							File.Copy(source.importFrom, destination);
							report.AddExportedEntry(source.importFrom, destination);
						} catch(Exception e) {
							report.AddErrorEntry(source.importFrom, destination, e.Message);
						}

						source.exportTo = destination;
					}
				}
			}

			AssetBundleBuildReport.AddExportReport(report);
		}

		public static bool ValidateExportPath (string currentExportFilePath, string combinedPath, Action NullOrEmpty, Action DoesNotExist) {
			if (string.IsNullOrEmpty(currentExportFilePath)) {
				NullOrEmpty();
				return false;
			}
			if (!Directory.Exists(combinedPath)) {
				DoesNotExist();
				return false;
			}
			return true;
		}
	}
}