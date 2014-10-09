using MonoDevelop.Components.Commands;
using MonoDevelop.Core;
using MonoDevelop.Ide;
using MonoDevelop.Ide.Codons;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Ide.Gui.Pads;
using MonoDevelop.Projects;
using MonoDevelop.UnityMode.UnityRestClient;
using System.Linq;
using System;
using System.Collections.Generic;

namespace MonoDevelop.UnityMode
{
	public class StartupHandler : CommandHandler
	{
		private RestService restService;

		protected override void Run ()
		{
			Workbench workbench = IdeApp.Workbench;
			WorkbenchWindow workbenchWindow = workbench.RootWindow;

			var assetsFolderPad = workbench.GetPad<AssetsFolderPad>();

			if (assetsFolderPad != null && !assetsFolderPad.Visible)
				assetsFolderPad.Visible = true;

			var solutionPad = workbench.Pads.SolutionPad;

			if (solutionPad != null && solutionPad.Visible)
				solutionPad.Visible = false;

			workbenchWindow.FocusInEvent += WorkbenchFocusInEvent;

			SetupSettingsFromArgs ();

			RestClient.SetServerUrl(UnityInstance.RestServerUrl);

			//Mono.Addins.AddinManager.AddinEngine.Registry.DisableAddin ("MonoDevelop.VersionControl");

			//IdeApp.CommandService.CommandEntrySetPostProcessor += MyPostProcessor;

			//((DefaultWorkbench)IdeApp.Workbench.RootWindow).RecreateMenu ();

			InitializeRestServiceAndPair ();
			IdeApp.Workbench.ShowCommandBar ("UnityDebugging");


			//var dw = (DefaultWorkbench)ww;

			/*
			Gtk.HBox contentBox = dw.Toolbar.ContentBox;
			//contentBox.PackStart(new Gtk.Label ("LUCASLUCAS"), false, false, 0);
			//contentBox.PackStart(new Gtk.Button ("LUCASLUCAS"), false, false, 0);

			var statusAreaAlign = new Gtk.Alignment (100, 0, 1, 1);
			var button = new Gtk.Button ("AISHDASDADAS");
			button.Visible = true;
			statusAreaAlign.Add (button);
			contentBox.PackStart (statusAreaAlign, true, true, 0);
			contentBox.PackStart (button, false, false, 10);
			//MonoDevelop.Debugger.DebuggerService;

			dw.Toolbar.unityDebugButton.Toggled += (sender, e) => {if (true) {
					DebugEditorHandler.Doit();
					}
				};*/
		}


		CommandEntrySet MyPostProcessor(CommandEntrySet input)
		{
			var toRemove = new List<CommandEntry> ();
			foreach(CommandEntry ce in input)
			{
				var theSet = ce as CommandEntrySet;
				if (theSet != null)
					MyPostProcessor (theSet);

				var id = ce.CommandId as string;
				if (isBlackListed (id))
					toRemove.Add (ce);
			}
//			foreach (var r in toRemove)
//				input.Remove (r);

			return input;
		}

		bool isBlackListed(string command)
		{
			switch (command)
			{
			/*			case "MonoDevelop.Ide.Commands.ProjectCommands.CleanSolution":
			case "MonoDevelop.Ide.Commands.ProjectCommands.RebuildSolution":
			case "MonoDevelop.Ide.Commands.ProjectCommands.Rebuild":
			case "MonoDevelop.Ide.Commands.ProjectCommands.Clean":*/
			case "Project":
			case "Build":
			case "Tools":
			case "RecentProjects":
			case "MonoDevelop.Ide.Commands.FileCommands.NewProject":
			case "MonoDevelop.Ide.Commands.FileCommands.NewWorkspace":
			case "MonoDevelop.Ide.Commands.FileCommands.CloseWorkspace":
			case "MonoDevelop.Ide.Commands.ViewCommands.ShowWelcomePage":
			case "MonoDevelop.Ide.Commands.HelpCommands.About":
			case "MonoDevelop.Ide.Updater.UpdateCommands.CheckForUpdates":
				return true;
			default:
				return false;
			}
		}

		void SetupSettingsFromArgs ()
		{
			var args = Environment.GetCommandLineArgs ();

			string openFileArg = null;

			var p = new Mono.Options.OptionSet ();
			p.Add ("unityProcessId=", "Unity Process Id", (int i) => UnityInstance.ProcessId = i);
			p.Add ("unityRestServerUrl=", "Unity REST Server URL", s => UnityInstance.RestServerUrl = s);
			p.Add ("unityOpenFile=", "Unity Open File", f => openFileArg = f);

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
		 
		void OpenFile(string filename, int line)
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

		void UnityPairRequest(int unityProcessId, string unityRestServerUrl, string unityProject)
		{
			UnityInstance.ProcessId = unityProcessId;
			UnityInstance.RestServerUrl = unityRestServerUrl;
			UnityInstance.Project = unityProject;

			RestClient.SetServerUrl (UnityInstance.RestServerUrl);

			LoggingService.LogInfo("Received Pair request from Unity");
			UnityInstance.Log();

			UnityModeAddin.UpdateUnityProjectState ();
		}

		void QuitApplicationRequest(string unityProject)
		{
			if(unityProject == UnityInstance.Project)
			{
				IdeApp.Exit();
			}
		} 
			
		void InitializeRestServiceAndPair()
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

		private void WorkbenchFocusInEvent(object o, Gtk.FocusInEventArgs args)
		{
			UnityModeAddin.UpdateUnityProjectState();
		}
	}
}
