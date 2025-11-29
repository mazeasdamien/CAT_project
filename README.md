# CAT Project

This Unity project is designed to interface with industrial robotic systems, specifically Fanuc robots, using the Data Distribution Service (DDS) for real-time communication. It allows for the visualization and control of robot states within a Unity environment based on data received from external DDS publishers.

## DDS Integration

The core communication layer is built upon RTI Connext DDS, managed through a set of C# scripts located in `Assets/DDS`.

### Script Descriptions

#### 1. `DDSHandler.cs`
**Role:** Central DDS Manager / Singleton

The `DDSHandler` class is the backbone of the DDS integration in this project. It is responsible for the lifecycle management of the DDS Domain Participant and provides utility methods for creating readers and writers.

*   **Initialization**: It initializes the DDS `DomainParticipant` using a specific Quality of Service (QoS) profile. By default, it loads from `QOS.xml` using the library `RigQoSLibrary` and profile `RigQoSProfile`. These settings are **configurable via the Unity Inspector**.
*   **Singleton Pattern**: Implements the Singleton pattern to ensure only one instance of the DDS handler exists throughout the application lifecycle and persists across scene loads.
*   **Performance Optimizations**: 
    *   **Shared Entities**: It pre-creates and reuses a shared `Publisher` and `Subscriber` to reduce the overhead of creating these heavy entities for every new topic.
    *   **String Caching**: QoS profile strings are cached to avoid repeated string allocations.
*   **Topic Management**: It maintains a registry of created topics to prevent duplicate topic creation, which ensures stability when multiple components request the same topic.
*   **Reader/Writer Creation**: Provides public methods (`SetupDataReader`, `SetupDataWriter`) that other scripts can use to easily create DDS DataReaders and DataWriters for specific topics and dynamic data types without handling the low-level boilerplate.
*   **Cleanup**: Handles the proper disposal of the `DomainParticipant` when the application quits to prevent resource leaks.

#### 2. `FanucManager.cs`
**Role:** Robot State Consumer & Visualizer

The `FanucManager` class is a specialized component that consumes data specifically for a Fanuc robot. It subscribes to robot state updates and applies them to a Unity ArticulationBody system.

*   **Data Structure Definition**: It dynamically defines the `RobotState` struct type using `DynamicTypeFactory`. This struct includes:
    *   **Joints**: `J1`, `J2`, `J3`, `J4`, `J5`, `J6` (Joint angles)
    *   **Cartesian Position**: `X`, `Y`, `Z`
    *   **Orientation (Fanuc WPR)**: `W` (Yaw), `P` (Pitch), `R` (Roll)
*   **Subscription**: Uses `DDSHandler` to subscribe to the `RobotState_Topic` (configurable in the Inspector).
*   **Performance Optimizations**: 
    *   **Member ID Caching**: Caches the integer IDs of the dynamic data members during initialization to avoid expensive string-based lookups in the `Update` loop.
*   **Data Processing**: In every frame, it checks for new data samples. If valid data is received, it:
    *   Updates the **World Position** transform (converting millimeters to meters).
    *   Updates the **World Orientation** by converting Fanuc WPR angles to a Unity Quaternion.
    *   Updates the **Joint Angles** of the assigned `ArticulationBody` components.
*   **Coupling Compensation**: Implements specific logic for Fanuc robots where the J3 joint angle is mechanically coupled with J2 (applying `J3 + J2`).

### Mathematical Conversions

The `FanucManager` script applies several mathematical transformations to map the industrial robot data to the Unity coordinate system.

#### 1. Coordinate System Conversion
Fanuc robots typically use a Right-Handed coordinate system in millimeters, while Unity uses a Left-Handed coordinate system in meters.

$$
\begin{aligned}
X_{Unity} &= -\frac{X_{Fanuc}}{1000} \\
Y_{Unity} &= \frac{Y_{Fanuc}}{1000} \\
Z_{Unity} &= \frac{Z_{Fanuc}}{1000}
\end{aligned}
$$

#### 2. Orientation (Fanuc WPR to Quaternion)
Fanuc uses Yaw-Pitch-Roll (W-P-R) Euler angles. These are converted to a Quaternion $(q_x, q_y, q_z, q_w)$ for Unity.

First, angles are converted to radians:

$$
\theta_{rad} = \theta_{deg} \times \frac{\pi}{180}
$$

Then, the half-angle formulas are applied:

$$
\begin{aligned}
q_x &= \cos(R/2)\cos(P/2)\sin(W/2) - \sin(R/2)\sin(P/2)\cos(W/2) \\
q_y &= \cos(R/2)\sin(P/2)\cos(W/2) + \sin(R/2)\cos(P/2)\sin(W/2) \\
q_z &= \sin(R/2)\cos(P/2)\cos(W/2) - \cos(R/2)\sin(P/2)\sin(W/2) \\
q_w &= \cos(R/2)\cos(P/2)\cos(W/2) + \sin(R/2)\sin(P/2)\sin(W/2)
\end{aligned}
$$

#### 3. Joint Coupling
For this specific Fanuc model, the third joint ($J3$) is mechanically coupled to the second joint ($J2$). The script compensates for this:

$$
J3_{Unity} = J3_{Fanuc} + J2_{Fanuc}
$$

## Requirements

*   **RTI Connext DDS**: The project requires the RTI Connext DDS libraries to be present in the project (typically under `Packages` or `Assets/Plugins`).
*   **QoS Profile**: A valid `QOS.xml` file must be present in the project root to define the `RigQoSLibrary::RigQoSProfile` (or whatever is configured in the Inspector).
*   **RTI License**: A valid license file (`rti_license.dat`) must be present in the project root.
