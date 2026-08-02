"""Static guardrails for WPF accessibility; clean-VM UIA remains the runtime gate."""

from pathlib import Path
import xml.etree.ElementTree as ET


XAML = Path(__file__).resolve().parents[1] / "WinCarePro.Desktop" / "MainWindow.xaml"
PRESENTATION = "http://schemas.microsoft.com/winfx/2006/xaml/presentation"
X = "http://schemas.microsoft.com/winfx/2006/xaml"
AUTOMATION = "clr-namespace:System.Windows.Automation;assembly=PresentationCore"


def load_root():
    return ET.parse(XAML).getroot()


def elements(root, name):
    return root.iter(f"{{{PRESENTATION}}}{name}")


def test_every_interactive_control_has_an_accessible_name_and_focus_style():
    root = load_root()
    controls = [*elements(root, "Button"), *elements(root, "ComboBox"), *elements(root, "ListBox")]

    assert controls
    for control in controls:
        assert control.get(f"{{{AUTOMATION}}}AutomationProperties.Name")
        assert control.get("Style") in {
            "{StaticResource AccessibleButton}",
            "{StaticResource AccessibleNavButton}",
            "{StaticResource AccessibleSelector}",
        }


def test_status_is_a_named_polite_live_region():
    root = load_root()
    status = next(item for item in elements(root, "TextBlock") if item.get(f"{{{X}}}Name") == "StatusText")

    assert status.get(f"{{{AUTOMATION}}}AutomationProperties.Name") == "Guided Care status"
    assert status.get(f"{{{AUTOMATION}}}AutomationProperties.LiveSetting") == "Polite"


def test_keyboard_shortcuts_focus_visual_and_high_contrast_contract_exist():
    root = load_root()
    bindings = {(item.get("Key"), item.get("Modifiers")) for item in elements(root, "KeyBinding")}
    setters = {(item.get("Property"), item.get("Value")) for item in elements(root, "Setter")}
    triggers = list(elements(root, "DataTrigger"))

    assert {("D", "Control"), ("S", "Control")} <= bindings
    assert ("FocusVisualStyle", "{StaticResource KeyboardFocusStyle}") in setters
    assert any("HighContrast" in (trigger.get("Binding") or "") for trigger in triggers)
