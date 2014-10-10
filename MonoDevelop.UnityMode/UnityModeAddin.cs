using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using MonoDevelop.Components.Commands;
using MonoDevelop.Core;
using MonoDevelop.Ide;
using MonoDevelop.Ide.Codons;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Ide.Gui.Pads;
using MonoDevelop.Projects;
using MonoDevelop.UnityMode.RestServiceModel;
using MonoDevelop.UnityMode.UnityRestClient;

namespace MonoDevelop.UnityMode
{
	public static class UnityModeAddin
	{
		static RestService restService;
		static UnityProjectState unityProjectState;
		public static event UnityProjectStateChangedHandler UnityProjectStateChanged;

		static UnityModeAddin()
		{
			UnityProjectState = new UnityProjectState ();
		}

		public static void Initialize()
		{
			UnitySolution = new UnitySolution { Name = "UnitySolution" };

			// TODO: Should we close all other open solutions?

			UnityProjectStateChanged += (sender, e) =>
			{
				SolutionUpdater.Update(UnitySolution, e.State);

				if(!IdeApp.Workspace.Items.Contains(UnitySolution))
					IdeApp.Workspace.Items.Insert(0, UnitySolution);
			};
		}

		public static void ClearUnityProjectState()
		{
			UnityProjectState = new UnityProjectState ();
			NotifyUnityProjectStateChanged ();
		}

		public static UnitySolution UnitySolution { get; private set; }

		public static void NotifyUnityProjectStateChanged()
		{
			if (UnityProjectStateChanged != null)
				UnityProjectStateChanged(null, new UnityProjectStateChangedEventArgs() { State = unityProjectState });
		}

		public static void UpdateUnityProjectState()
		{
			if(UnityInstance.ProcessId > 0)
			{
				try
				{
					Process unityProcess = Process.GetProcessById(UnityInstance.ProcessId);
				}
				catch(Exception)
				{
					UnityInstance.ProcessId = -1;
					UnityInstance.RestServerUrl = null;

					RestClient.SetServerUrl (null);

					ClearUnityProjectState ();
					return;
				}
			}

			if (!RestClient.Available)
				return;

			DispatchService.BackgroundDispatch(() =>
			{
				LoggingService.LogInfo("Sending Unity Project request");
				UnityModeAddin.UnityProjectState = RestClient.GetUnityProjectState();
			});
		}

		public static void UpdateUnityProjectStateRename(string oldPath, string newPath)
		{
			if (!RestClient.Available)
				return;

			DispatchService.BackgroundDispatch(() =>
			{
				LoggingService.LogInfo("Sending Unity Project request (rename)");
				var projectState = RestClient.GetUnityProjectState();

				projectState.RenameHint = new RenameHint {OldPath = oldPath, newPath = newPath};

				UnityProjectState = projectState;
			});
		}

		public static UnityProjectState UnityProjectState 
		{
			get { return unityProjectState; }
			set {
				unityProjectState = value;
				NotifyUnityProjectStateChanged();
			}
		}

		internal static void InitializeRestServiceAndPair()
		{
			UnityModeAddin.Initialize ();

			restService = new RestService ( fileOpenRequest => OpenFile(fileOpenRequest.File, fileOpenRequest.Line), 
				pairRequest => UnityPairRequest(pairRequest.UnityProcessId, pairRequest.UnityRestServerUrl, pairRequest.UnityProject),
				quitRequest => QuitApplicationRequest(quitRequest.UnityProject));

			DispatchService.BackgroundDispatch(() =>
				{
					LoggingService.LogInfo("Sending Pair request to Unity");
					var result = RestClient.Pair(restService.Url, MonoDevelop.Core.BrandingService.ApplicationName + " " + MonoDevelop.BuildInfo.VersionLabel);
					LoggingService.LogInfo("Unity Pair Request Result: " + result.result);

					UnityInstance.ProcessId = result.unityprocessid;
					UnityInstance.Project = result.unityproject;

					UnityInstance.Log();
				});

			UnityModeAddin.UpdateUnityProjectState();
		}

		internal static void SetupSettingsFromArgs ()
		{
			var args = Environment.GetCommandLineArgs ();

			string openFileArg = null;

			var p = new Mono.Options.OptionSet ();
			p.Add ("unityProcessId=", "Unity Process Id", (int i) => UnityInstance.ProcessId = i);
			p.Add ("unityRestServerUrl=", "Unity REST Server URL", s => UnityInstance.RestServerUrl = s);
			p.Add ("unityOpenFile=", "Unity Open File", f => openFileArg = f);

			RestClient.SetServerUrl(UnityInstance.RestServerUrl);

			LoggingService.Log (MonoDevelop.Core.Logging.LogLevel.Info, "UnityMode Args " + String.Join("!",args));

			try 
			{
				p.Parse (args);
			} 
			catch(Mono.Options.OptionException e)
			{
				LoggingService.LogInfo("OptionException: " + e.ToString());
			}

			if (openFileArg != null && openFileArg.Length > 0) 
			{
				string[] fileLine = openFileArg.Split (';');

				if (fileLine.Length == 2)
					OpenFile (fileLine[0], int.Parse(fileLine[1]));
				else
					OpenFile (openFileArg, 0);
			}

			UnityInstance.Log();
		}

		static void OpenFile(string filename, int line)
		{
			LoggingService.LogInfo ("OpenFile: " + filename + " Line " + line);

			var fileOpenInformation = new FileOpenInformation (filename, null, line, 0, OpenDocumentOptions.BringToFront);

			try
			{
				DispatchService.GuiDispatch(() =>
					{
						if (IdeApp.Workbench.Documents.Any(d => d.FileName == fileOpenInformation.FileName))
						{
							var docs = IdeApp.Workbench.Documents.Where(d => d.FileName == fileOpenInformation.FileName);
							docs.ElementAt(0).Select();
						}
						else
						{
							IdeApp.Workbench.OpenDocument(fileOpenInformation);
							DispatchService.GuiDispatch(IdeApp.Workbench.GrabDesktopFocus);

						}
					});
			}
			catch (Exception e)
			{
				LoggingService.LogError(e.ToString());
			}
		}

		internal static void UpdateUnityOpenDocuments()
		{
			if (!UnityInstance.OpenDocuments.Any (d => d == IdeApp.Workbench.ActiveDocument.FileName.ToString().Replace('\\', '/'))) 
			{
				UnityInstance.OpenDocuments = IdeApp.Workbench.Documents.Select (d => d.FileName.ToString().Replace('\\', '/')).ToList ();
			}
		}

		static void UnityPairRequest(int unityProcessId, string unityRestServerUrl, string unityProject)
		{
			UnityInstance.ProcessId = unityProcessId;
			UnityInstance.RestServerUrl = unityRestServerUrl;
			UnityInstance.Project = unityProject;

			RestClient.SetServerUrl (UnityInstance.RestServerUrl);

			LoggingService.LogInfo("Received Pair request from Unity");
			UnityInstance.Log();

			UnityModeAddin.UpdateUnityProjectState ();
		}

		static void QuitApplicationRequest(string unityProject)
		{
			if(unityProject == UnityInstance.Project)
			{
				IdeApp.Exit();
			}
		} 
	}

	public delegate void UnityProjectStateChangedHandler(object sender, UnityProjectStateChangedEventArgs e);

	public class UnityProjectStateChangedEventArgs : EventArgs
	{
		public UnityProjectState State;
	}

}

