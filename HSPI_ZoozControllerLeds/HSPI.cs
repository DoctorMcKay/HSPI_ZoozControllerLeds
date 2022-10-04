using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HomeSeer.Jui.Views;
using HomeSeer.PluginSdk;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Logging;

namespace HSPI_ZoozControllerLeds {
	public class HSPI : AbstractPlugin {
		public override string Name { get; } = "Zooz Controller LEDs";
		public override string Id { get; } = "ZoozControllerLeds";

		public IHsController HsController => HomeSeerSystem;
		public readonly List<int> ZoozDevices = new List<int>();

		private bool _debugLogging = false;

		protected override void Initialize() {
			WriteLog(ELogType.Debug, "Initialize");
			
			Status = PluginStatus.Info("Initializing...");
			
			ActionTypes.AddActionType(typeof(StatusLedAction));

			WriteLog(ELogType.Trace, "Enumerating HS devices to find Zooz devices");
			DateTime start = DateTime.Now;
			foreach (HsDevice device in HomeSeerSystem.GetAllDevices(false).Where(_isZoozController)) {
				ZoozDevices.Add(device.Ref);
				WriteLog(ELogType.Info, $"Detected device {device.Ref} {NameDevice(device)}");
			}

			double time = DateTime.Now.Subtract(start).TotalMilliseconds;
			WriteLog(ELogType.Info, $"Initialization complete in {time} ms. Found {ZoozDevices.Count} Zooz devices");
			Status = PluginStatus.Ok();
		}

		public string NameDevice(HsDevice device) {
			return HomeSeerSystem.IsLocation1First()
				? $"{device.Location} {device.Location2} {device.Name}".Trim()
				: $"{device.Location2} {device.Location} {device.Name}".Trim();
		}

		private bool _isZoozController(HsDevice device) {
			if (device.Interface != "Z-Wave") {
				return false;
			}

			int? manufacturerId = device.PlugExtraData.GetNamed<int?>("manufacturer_id");
			ushort? prodType = device.PlugExtraData.GetNamed<ushort?>("manufacturer_prod_type");
			ushort? prodId = device.PlugExtraData.GetNamed<ushort?>("manufacturer_prod_id");

			if (manufacturerId != 0x027A) {
				// Wrong manufacturer id, not zooz
				return false;
			}

			if (prodId != 0xA008) {
				// Wrong product id
				return false;
			}

			if (prodType != 0x7000) {
				// Wrong product type
				return false;
			}

			return true;
		}
		
		internal void ConfigSet(string homeId, byte nodeId, byte configProperty, byte valueLength, int value) {
			DateTime start = DateTime.Now;
			object result = HomeSeerSystem.PluginFunction("Z-Wave", "SetDeviceParameterValue", new object[] {
				homeId,
				nodeId,
				configProperty,
				valueLength,
				value
			});
			
			double ms = DateTime.Now.Subtract(start).TotalMilliseconds;
			if (ms > 2000) {
				double sec = Math.Round(ms / 1000, 1);
				WriteLog(ELogType.Warning, $"Node {homeId}:{nodeId} was very slow to respond ({sec} sec) and might need to be optimized.");
			}

			string printableResult = (string) result;
			WriteLog(ELogType.Debug, $"Set {homeId}:{nodeId}:{configProperty} = {value}:{valueLength} with result {printableResult} in {ms} ms");
		}

		protected override bool OnSettingChange(string pageId, AbstractView currentView, AbstractView changedView) {
			return true;
		}

		protected override void BeforeReturnStatus() { }
		
		public void WriteLog(ELogType logType, string message, [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string caller = null) {
			#if DEBUG
			bool isDebugMode = true;

			// Prepend calling function and line number
			message = $"[{caller}:{lineNumber}] {message}";
			
			// Also print to console in debug builds
			string type = logType.ToString().ToLower();
			Console.WriteLine($"[{type}] {message}");
			#else
			if (logType == ELogType.Trace) {
				// Don't record Trace events in production builds even if debug logging is enabled
				return;
			}

			bool isDebugMode = _debugLogging;
			#endif

			if (logType <= ELogType.Debug && !isDebugMode) {
				return;
			}
			
			HomeSeerSystem.WriteLog(logType, message, Name);
		}
	}
}