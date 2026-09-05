// SPDX-License-Identifier: MPL-2.0
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FixWorld.Core
{
	/// <summary>
	/// This is added as a component to the GameObject on scene to forward events to the controller.
	/// </summary>
	public class UnityProxyComponent : MonoBehaviour
	{
		public FixWorldController controllerInstance;

		public void Start()
		{
			controllerInstance = FixWorldController.Instance;
		}

		public void OnEnable()
		{
			SceneManager.sceneLoaded += OnSceneLoaded;
		}

		public void OnDisable()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
		}

		public void FixedUpdate()
		{
			controllerInstance.OnFixedUpdate();
		}

		private void OnApplicationQuit()
		{
			controllerInstance.OnApplicationQuit();
		}

		public void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
		{
			controllerInstance.OnSceneLoaded(scene);
		}
	}
}
