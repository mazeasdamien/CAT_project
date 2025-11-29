# CAT Project

This Unity project is designed to interface with industrial robotic systems, specifically Fanuc robots, using the Data Distribution Service (DDS) for real-time communication. It allows for the visualization and control of robot states within a Unity environment based on data received from external DDS publishers.

## DDS Integration

The core communication layer is built upon RTI Connext DDS, managed through a set of C# scripts located in `Assets/DDS`.

### Script Descriptions

#### 1. `DDSHandler.cs`
**Role:** Central DDS Manager / Singleton

The `DDSHandler` class is the backbone of the DDS integration in this project. It is responsible for the lifecycle management of the DDS Domain Participant and provides utility methods for creating readers and writers.

*   **Initialization**: It initializes the DDS `DomainParticipant` using a specific Quality of Service (QoS) profile (`CAT_Industrial_Library::SafetyCritical_Profile`) defined in `USER_QOS_PROFILES.xml`.
*   **Singleton Pattern**: Implements the Singleton pattern to ensure only one instance of the DDS handler exists throughout the application lifecycle.
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
*   **Subscription**: Uses `DDSHandler` to subscribe to the `RobotState_Topic`.
*   **Data Processing**: In every frame, it checks for new data samples. If valid data is received, it:
    *   Updates the **World Position** transform (converting millimeters to meters).
    *   Updates the **World Orientation** by converting Fanuc WPR angles to a Unity Quaternion.
    *   Updates the **Joint Angles** of the assigned `ArticulationBody` components.
*   **Coupling Compensation**: Implements specific logic for Fanuc robots where the J3 joint angle is mechanically coupled with J2 (applying `J3 + J2`).

## Requirements

*   **RTI Connext DDS**: The project requires the RTI Connext DDS libraries to be present in the project (typically under `Packages` or `Assets/Plugins`).
*   **QoS Profile**: A valid `USER_QOS_PROFILES.xml` file must be present in the project root to define the `CAT_Industrial_Library::SafetyCritical_Profile`.
