using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using HomeSeer.Jui.Views;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Events;
using HomeSeer.PluginSdk.Logging;
using HSPI_ZoozControllerLeds.Enums;

namespace HSPI_ZoozControllerLeds {
	public class StatusLedAction : AbstractActionType {
		private HSPI Listener => ActionListener as HSPI;
		
		private string InputIdDeviceSelect => $"{PageId}_Device";
		private string InputIdWhichLed => $"{PageId}_LedIndex";
		private string InputIdLedColor => $"{PageId}_LedColor";

		private static readonly Dictionary<int, SemaphoreSlim> DeviceControlSemaphores = new Dictionary<int, SemaphoreSlim>();

		public StatusLedAction() { }

		public StatusLedAction(int id, int eventRef, byte[] dataIn, ActionTypeCollection.IActionTypeListener listener, bool logDebug = false)
			: base(id, eventRef, dataIn, listener, logDebug) { }

		protected override string GetName() {
			return "Z-Wave: Set Zooz ZEN32 LED";
		}

		protected override void OnNewAction() {
			ConfigPage = _initNewConfigPage().Page;
		}

		public override bool IsFullyConfigured() {
			return !string.IsNullOrEmpty(_getFieldValue(InputIdDeviceSelect))
			       && !string.IsNullOrEmpty(_getFieldValue(InputIdWhichLed))
			       && !string.IsNullOrEmpty(_getFieldValue(InputIdLedColor));
		}

		protected override bool OnConfigItemUpdate(AbstractView configViewChange) {
			// Update the selection in our config page so that _getFieldValue will work in our factory methods
			ConfigPage.UpdateViewById(configViewChange);

			if (configViewChange.Id == InputIdDeviceSelect) {
				ConfigPage = _initConfigPageWithLedIndex().Page;
			} else if (configViewChange.Id == InputIdWhichLed) {
				ConfigPage = _initConfigPageWithColor().Page;
			}

			// We can return false here since we already did ConfigPage.UpdateViewById, which is all returning true here does
			return false;
		}

		public override string GetPrettyString() {
			StringBuilder builder = new StringBuilder();
			builder.Append("Set ");
			builder.Append(_getFieldDisplayValue(InputIdDeviceSelect));
			builder.Append(" - ");
			builder.Append(_getFieldDisplayValue(InputIdWhichLed));
			builder.Append(" to ");
			builder.Append(_getFieldDisplayValue(InputIdLedColor));
			return builder.ToString();
		}

		public override bool OnRunAction() {
			int deviceRef = int.Parse(_getFieldValue(InputIdDeviceSelect));
			if (!DeviceControlSemaphores.ContainsKey(deviceRef)) {
				DeviceControlSemaphores.Add(deviceRef, new SemaphoreSlim(1));
			}

			DeviceControlSemaphores[deviceRef].WaitAsync().ContinueWith(_ => {
				try {
					HsDevice device = Listener.HsController.GetDeviceByRef(deviceRef);

					string[] address = device.Address.Split('-');
					string homeId = address[0];
					byte nodeId = byte.Parse(address[1]);

					byte buttonIndex = (byte) (byte.Parse(_getFieldValue(InputIdWhichLed)) - 1);
					ActionColorChoice color = (ActionColorChoice) byte.Parse(_getFieldValue(InputIdLedColor));

					if (color == ActionColorChoice.Off) {
						// We need to set color, then on
						Listener.ConfigSet(homeId, nodeId, (byte) ((byte) ConfigParam.LedModeButton1 + buttonIndex), 1, (byte) LedMode.AlwaysOff);
						Listener.ConfigSet(homeId, nodeId, (byte) ((byte) ConfigParam.LedColorButton1 + buttonIndex), 1, (byte) LedColor.White);
					} else {
						// We need to set color, then on
						byte colorValue = (byte) ((byte) color - 1);
						Listener.ConfigSet(homeId, nodeId, (byte) ((byte) ConfigParam.LedColorButton1 + buttonIndex), 1, colorValue);
						Listener.ConfigSet(homeId, nodeId, (byte) ((byte) ConfigParam.LedModeButton1 + buttonIndex), 1, (byte) LedMode.AlwaysOn);
					}
				} catch (Exception ex) {
					Listener.WriteLog(ELogType.Error, ex.Message);
				}

				DeviceControlSemaphores[deviceRef].Release();
			});

			return true;
		}

		public override bool ReferencesDeviceOrFeature(int devOrFeatRef) {
			return int.Parse(_getFieldValue(InputIdDeviceSelect)) == devOrFeatRef;
		}

		private int _getFieldSelection(string fieldId) {
			return !ConfigPage.ContainsViewWithId(fieldId) ? -1 : ((SelectListView) ConfigPage.GetViewById(fieldId)).Selection;
		}

		private string _getFieldValue(string fieldId) {
			return !ConfigPage.ContainsViewWithId(fieldId) ? "" : ((SelectListView) ConfigPage.GetViewById(fieldId)).GetSelectedOptionKey();
		}
		
		private string _getFieldDisplayValue(string fieldId) {
			return !ConfigPage.ContainsViewWithId(fieldId) ? "" : ((SelectListView) ConfigPage.GetViewById(fieldId)).GetSelectedOption();
		}

		private PageFactory _initNewConfigPage() {
			Dictionary<string, int> deviceList = new Dictionary<string, int>();

			foreach (int devRef in Listener.ZoozDevices) {
				HsDevice device = Listener.HsController.GetDeviceByRef(devRef);
				deviceList.Add(Listener.NameDevice(device), devRef);
			}

			List<string> deviceNames = new List<string>(deviceList.Keys);
			deviceNames.Sort();

			List<string> deviceListOptions = new List<string>();
			List<string> deviceListOptionsKeys = new List<string>();

			foreach (string deviceName in deviceNames) {
				deviceListOptions.Add(deviceName);
				deviceListOptionsKeys.Add(deviceList[deviceName].ToString());
			}
			
			return PageFactory.CreateEventActionPage(PageId, "Action")
				.WithDropDownSelectList(InputIdDeviceSelect, "Device", deviceListOptions, deviceListOptionsKeys, _getFieldSelection(InputIdDeviceSelect));
		}

		private PageFactory _initConfigPageWithLedIndex() {
			List<string> ledListOptions = new List<string> {
				"Large Button",
				"Small Button 1",
				"Small Button 2",
				"Small Button 3",
				"Small Button 4"
			};

			List<string> ledListOptionKeys = new List<string>();
			for (int i = 1; i <= 5; i++) {
				ledListOptionKeys.Add(i.ToString());
			}

			PageFactory factory = _initNewConfigPage();

			int whichLed = _getFieldSelection(InputIdWhichLed);
			factory.WithDropDownSelectList(InputIdWhichLed, "LED", ledListOptions, ledListOptionKeys, whichLed);

			return factory;
		}

		private PageFactory _initConfigPageWithColor() {
			List<string> colorOptions = new List<string>();
			List<string> colorOptionsKeys = new List<string>();

			foreach (ActionColorChoice color in Enum.GetValues(typeof(ActionColorChoice))) {
				colorOptions.Add(color.ToString());
				colorOptionsKeys.Add(((byte) color).ToString());
			}

			return _initConfigPageWithLedIndex()
				.WithDropDownSelectList(InputIdLedColor, "Color", colorOptions, colorOptionsKeys, _getFieldSelection(InputIdLedColor));
		}
	}
}
