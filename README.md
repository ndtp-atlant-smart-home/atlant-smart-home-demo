# Atlant Smart Home Demo

Unity demo of MQTT-connected smart home devices. The project simulates several appliances, synchronizes their state through an MQTT broker, and shows an in-world control tooltip for interaction.

## Overview

This project demonstrates how Unity can act as a smart device client for a home automation backend.

It currently includes:
- Light
- Coffee machine
- Microwave
- Washing machine
- Freezer
- Kettle

Common features:
- MQTT connection and session setup
- Stable per-device topics and pairing state
- State publishing and availability updates
- Device-specific simulated behavior
- On-screen tooltip controls with keyboard and click interaction

## How It Works

Each device inherits from `AtlantMqttDeviceBase`, which handles:
- connection to the broker
- topic resolution
- pairing flow
- JSON state publishing
- incoming command handling

Device classes implement their own state, simulation rules, and command mapping.

## Controls

Controls depend on the selected device, but the tooltip always shows the available actions.

Common patterns:
- `F` toggles the main power or start/stop action
- `1`, `2`, `3` adjust device-specific parameters
- Right click can be used on some appliances in the scene
- `E` closes the device tooltip

## Project Structure

- `Assets/Scripts/AtlantMqttDeviceBase.cs` - shared MQTT/device base logic
- `Assets/Scripts/AtlantLightDevice.cs` - light simulation
- `Assets/Scripts/AtlantCoffeeMachineDevice.cs` - coffee machine simulation
- `Assets/Scripts/AtlantMicrowaveDevice.cs` - microwave simulation
- `Assets/Scripts/AtlantWashingMachineDevice.cs` - washing machine simulation
- `Assets/Scripts/AtlantFreezerDevice.cs` - freezer simulation
- `Assets/Scripts/AtlantKettleDevice.cs` - kettle simulation
- `Assets/Scripts/AtlantDeviceTooltipUI.cs` - in-world control tooltip

## Requirements

- Unity project opened in a compatible Unity version
- MQTT broker available on the configured host and port
- Required packages already present in the project

## Notes

- Device topics and pairing data are kept stable across sessions when enabled in the inspector.
- The tooltip UI is designed to be used directly inside the scene.
- Some devices publish simulated telemetry over time, so their state changes even without user input.

## Screenshots

<p align="center">
  <img src="screenshots/full_scene.png" alt="Full scene" width="220" />
  <img src="screenshots/teapot_interface.png" alt="Kettle interface" width="220" />
  <img src="screenshots/microwave_interface.png" alt="Microwave interface" width="220" />
</p>

<p align="center">
  <img src="screenshots/wm_interface.png" alt="Washing machine interface" width="220" />
  <img src="screenshots/freezer_interface.png" alt="Freezer interface" width="220" />
  <img src="screenshots/teapot.png" alt="Kettle device" width="220" />
</p>

<p align="center">
  <img src="screenshots/microwave.png" alt="Microwave device" width="220" />
  <img src="screenshots/wm.png" alt="Washing machine device" width="220" />
  <img src="screenshots/freezer.png" alt="Freezer device" width="220" />
</p>
