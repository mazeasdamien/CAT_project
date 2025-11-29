# CAT Project - Digital Twin Interface

This project implements a real-time Digital Twin interface for industrial robotic systems, specifically Fanuc robots. It bridges the physical world (or simulated Roboguide environment) with a digital visualization in Unity using the Data Distribution Service (DDS) for high-performance, real-time communication.

## 🏗 System Architecture

The system consists of two main applications communicating via RTI Connext DDS:

1.  **WPF Application (`CAT_wpf_app`)**: Acts as the **Publisher**. It connects to the Fanuc Robot Controller (physical or virtual via Roboguide), reads the robot's state (Joints & Cartesian), and publishes this data to the DDS domain.
2.  **Unity Application (`CAT_unity_project`)**: Acts as the **Subscriber**. It receives the robot state updates and visualizes the robot's movement in real-time, handling coordinate system conversions and synchronization.

```mermaid
graph LR
    A[Fanuc Robot / Roboguide] -- FRRobot Library --> B[WPF App (Publisher)]
    B -- DDS Topic: RobotState_Topic --> C[Unity App (Subscriber)]
    C --> D[3D Visualization]
```

---

## 🖥️ WPF Application (Publisher)

Located in: `CAT_wpf_app/`

The WPF application is a standalone desktop tool responsible for data acquisition and transmission.

### Key Components
*   **`RobotStatePublisher.cs`**: The core logic script.
    *   **Data Acquisition**: Uses the `FRRobot` library to poll the robot's current position (`CurPosition`) including Joint angles and World (Cartesian) coordinates.
    *   **Dynamic Data**: Defines the `RobotState` data structure dynamically at runtime, eliminating the need for pre-compiled IDL files.
    *   **Change Detection**: Implements logic to only publish data when the robot's state has changed (threshold: `0.0001`), optimizing network bandwidth.
    *   **DDS Publication**: Publishes the data to the `RobotState_Topic`.
*   **`MainViewModel.cs`**: Handles the UI logic, user inputs for DDS configuration, and initiates the connection to the robot.

### Requirements
*   **Fanuc PCDK**: The PC Developer's Kit libraries (`FRRobot.dll`, etc.) must be referenced to communicate with the Fanuc controller.
*   **RTI Connext DDS**: The application requires the RTI DDS C# libraries.

---

## 🎮 Unity Application (Subscriber)

Located in: `CAT_unity_project/`

The Unity project visualizes the robot's movements based on the incoming DDS data.

### Key Scripts (Assets/DDS)

#### 1. `DDSHandler.cs`
**Role:** Central DDS Manager / Singleton

The `DDSHandler` class is the backbone of the DDS integration in Unity.
*   **Initialization**: Initializes the DDS `DomainParticipant` using settings from `QOS.xml` (configurable via Inspector).
*   **Singleton Pattern**: Ensures a single persistent instance across the application lifecycle.
*   **Performance**: Uses shared entities and string caching to minimize overhead and garbage collection.
*   **Topic Management**: Maintains a registry to prevent duplicate topic creation.
*   **Helper Methods**: Provides `SetupDataReader` and `SetupDataWriter` for easy integration by other scripts.

#### 2. `FanucManager.cs`
**Role:** Robot State Consumer & Visualizer

This script consumes the `RobotState` data and drives the 3D robot model.
*   **Dynamic Type Definition**: Reconstructs the `RobotState` struct to match the publisher's definition.
*   **Data Processing**:
    *   **Coordinate Conversion**: Converts Fanuc's Right-Handed (mm) system to Unity's Left-Handed (m) system.
    *   **Orientation**: Converts Fanuc WPR (Yaw-Pitch-Roll) Euler angles to Unity Quaternions.
    *   **Joint Coupling**: Compensates for the mechanical coupling between J2 and J3 specific to the Fanuc model ($J3_{Unity} = J3_{Fanuc} + J2_{Fanuc}$).
*   **Visualization**: Updates the `ArticulationBody` components for physics-based movement or standard `Transform` components.

### Mathematical Conversions

**Coordinate System (mm to m):**
$$
X_{Unity} = -X_{Fanuc}/1000, \quad Y_{Unity} = Y_{Fanuc}/1000, \quad Z_{Unity} = Z_{Fanuc}/1000
$$

**Orientation (WPR to Quaternion):**
Fanuc WPR angles are converted to Radians and then to a Unity Quaternion using half-angle formulas, adjusting for the coordinate system differences.

---

## 📡 DDS Configuration

The communication relies on a shared understanding of the data and Quality of Service (QoS).

### Data Structure (`RobotState`)
| Field | Type | Description |
| :--- | :--- | :--- |
| `Clock` | String | Timestamp of the sample |
| `Sample` | Int | Incremental sample ID |
| `J1` - `J6` | Double | Joint angles (Degrees) |
| `X`, `Y`, `Z` | Double | Cartesian Position (mm) |
| `W`, `P`, `R` | Double | Orientation (Degrees) |

### QoS Profile
Both applications load the QoS settings from `QOS.xml`. The default profile used is `RigQoSLibrary::RigQoSProfile`.
*   **Reliability**: Reliable (ensures data delivery).
*   **Durability**: Volatile (no history needed for real-time control, but can be adjusted).

---

## 🚀 Setup & Usage

1.  **Prerequisites**:
    *   Install **RTI Connext DDS**.
    *   Ensure `rti_license.dat` is present in both project root directories.
    *   Have **Fanuc Roboguide** running with a virtual robot OR a real Fanuc robot connected to the network.

2.  **Running the Publisher (WPF)**:
    *   Open `CAT_wpf_app.sln` in Visual Studio.
    *   Build and Run.
    *   Enter the Robot IP (or `127.0.0.1` for local Roboguide).
    *   Click **Connect** to start streaming.

3.  **Running the Subscriber (Unity)**:
    *   Open `CAT_unity_project` in Unity Hub.
    *   Open the main scene.
    *   Press **Play**.
    *   The virtual robot should now mirror the movements of the Fanuc robot.
