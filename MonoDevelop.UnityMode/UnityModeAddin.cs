using System;
using System.Diagnostics;
using MonoDevelop.Core;
using MonoDevelop.Ide;
using MonoDevelop.Ide.Gui;
using MonoDevelop.UnityMode.RestServiceModel;
using MonoDevelop.UnityMode.UnityRestClient;
using MonoDevelop.UnityMode.ServiceModel;

namespace MonoDevelop.UnityMode
{
	public static class UnityModeAddin
	{
		public static event UnityProjectStateChangedHandler UnityProjectStateChanged;

		static RestService restService;
		static UnityProjectState unityProjectState;
		static UnitySolution unitySolution;

		static UnityModeAddin ()
		{
			restService = new RestService 
			(
				fileOpenRequest => UnityRestHelpers.OpenFile(fileOpenRequest.File, fileOpenRequest.Line, OpenDocumentOptions.BringToFront),
				quitRequest => UnityRestHelpers.QuitApplication(quitRequest.UnityProject)
			);

			UnityProjectStateChanged += (sender, e) => {
				if (UnitySolution != null && e.State != null)
					SolutionUpdater.Update (UnitySolution, e.State);
			};
		}

		static UnityRestServiceSettings UnityRestServiceSettings { get; set; }

		static UnitySolution UnitySolution 
		{
			get { return unitySolution; }
			set 
			{
				unitySolution = value;
				IdeApp.Workspace.Items.Clear ();
				if(unitySolution != null)
					IdeApp.Workspace.Items.Add (unitySolution);
			}
		}

		public static UnityProjectSettings UnityProjectSettings { get; private set; }

		public static UnityProjectState UnityProjectState 
		{
			get { return unityProjectState; }

			private set 
			{
				unityProjectState = value;

				if (UnityProjectStateChanged != null)
					UnityProjectStateChanged(null, new UnityProjectStateChangedEventArgs() { State = unityProjectState });
			}
		}

		public static void OpenUnityProject(string projectPath)
		{
			InitializeAndPair (UnityRestServiceSettings.Load (projectPath).EditorRestServiceUrl);
		}

		internal static void InitializeAndPair(string unityRestServiceUrl)
		{
			UnityProjectSettings = new UnityProjectSettings ();
			UnitySolution = new UnitySolution { Name = "UnitySolution" };
			UnityProjectState = new UnityProjectState ();

			// FIXME: Unable to connect to own IP, might be blocked by Mongoose in Unity.
			var editorRestServiecUri = new Uri(unityRestServiceUrl);
			Pair ("http://localhost:"+editorRestServiecUri.Port, restService.Url);
		}

		static void Pair(string unityRestServiceUrl, string monoDevelopRestServiceUrl)
		{
			RestClient.SetServerUrl(unityRestServiceUrl);

			DispatchService.BackgroundDispatch(() =>
			{
				LoggingService.LogInfo("Sending Pair request to Unity");
				
				PairResult pairResult = null;

				try
				{
					pairResult = RestClient.Pair(monoDevelopRestServiceUrl, BrandingService.ApplicationName + " " + BuildInfo.VersionLabel);
					LoggingService.LogInfo("Unity Pair Request Result: " + pairResult.result);
				}
				catch(Exception e)
				{
					LoggingService.LogInfo("Unity Pair Request Exception: " + e);
					return;
				}
				
				UnityRestServiceSettings = new UnityRestServiceSettings(unityRestServiceUrl, pairResult.unityprocessid);
				UnityProjectSettings.ProjectPath = pairResult.unityproject;

				var projectSettings =  RestClient.GetProjectSettings();
				UnityProjectSettings.OpenDocuments = projectSettings.documents;
				UnityProjectSettings.Breakpoints = projectSettings.breakpoints;

				foreach(var document in UnityProjectSettings.OpenDocuments)
					UnityRestHelpers.OpenFile(document, 0);
				
				UnityProjectStateRefresh ();
			});
		}

		static void ShutdownAndUnpair()
		{
			UnityProjectSettings = null;
			UnitySolution = null;
			UnityProjectState = null;
			RestClient.SetServerUrl (null);
		}

		public static void UnityProjectStateRefresh ()
		{
			if (!Paired)
				return;

			if (!IsUnityRunning()) 
			{
				ShutdownAndUnpair ();
				return;
			}

			DispatchService.BackgroundDispatch(() =>
			{
				LoggingService.LogInfo("Sending Unity Project request");
				UnityProjectState = RestClient.GetUnityProjectState();
			});
		}

		public static void UnityProjectStateRefreshRename (string oldPath, string newPath)
		{
			if (!Paired)
				return;

			if (!IsUnityRunning()) 
			{
				ShutdownAndUnpair ();
				return;
			}

			DispatchService.BackgroundDispatch(() =>
			{
				LoggingService.LogInfo("Sending Unity Project request (rename)");
				var projectState = RestClient.GetUnityProjectState();

				projectState.RenameHint = new RenameHint {OldPath = oldPath, NewPath = newPath};

				UnityProjectState = projectState;
			});
		}

		static bool Paired
		{
			get { return UnityRestServiceSettings != null && UnityRestServiceSettings.EditorProcessID > 0 && RestClient.Available; }
		}

		static bool IsUnityRunning()
		{
			if (UnityRestServiceSettings == null)
				return false;

			try
			{
				Process.GetProcessById(UnityRestServiceSettings.EditorProcessID);
			}
			catch(Exception)
			{
				return false;
			}

			return true;
		}
	}
}

