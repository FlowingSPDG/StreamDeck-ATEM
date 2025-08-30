# StreamDeck ATEM Plugin

A powerful Elgato StreamDeck plugin that provides direct control over Blackmagic Design ATEM video switchers. This plugin allows you to control your ATEM switcher directly from your StreamDeck, making live production workflows more efficient and streamlined.

## 🎯 Features

### Current Features
- **ATEM Connection Management**: Automatic connection to ATEM switchers via IP address with connection pooling and automatic reconnection
- **Input Source Controls**:
  - **Preview Action**: Set preview inputs with tally support (GREEN indicator)
  - **Program Action**: Set program inputs with tally support (RED indicator)
- **Transition Controls**:
  - **Cut Action**: Performs immediate cut transitions
  - **Auto Transition Action**: Executes auto transitions with current settings
  - **Set Next Transition Action**: Configure transition style and duration
- **Real-time Tally Support**: Visual feedback with GREEN/RED button states
- **Real-time Status**: Connection status monitoring and automatic state synchronization
- **Multi-Mix Effect Support**: Control up to 4 Mix Effect blocks
- **Dynamic Input Discovery**: Automatic detection of available inputs from ATEM switcher

### Advanced Features
- **Connection Pooling**: Efficient resource management for multiple actions using the same ATEM
- **Global State Management**: Centralized state tracking and event distribution
- **Automatic Reconnection**: Built-in retry logic for network interruptions
- **Input Caching**: Dynamic input list updates based on switcher capabilities

## 📋 Requirements

### System Requirements
- **Operating System**: Windows 10 x64 or later
- **StreamDeck Software**: Version 6.4 or higher
- **Network**: ATEM switcher must be accessible via network (WiFi or Ethernet)

### Hardware Requirements
- **Elgato StreamDeck**: Any StreamDeck model (Standard, XL, Mini, or Plus)
- **ATEM Switcher**: Any Blackmagic Design ATEM model with SDK support
- **Network Connection**: Wired or wireless connection to ATEM switcher

### Important Note
- **ATEM Software Control**: **NOT required** - This plugin connects directly to the ATEM via the SDK

## 🚀 Installation

### Prerequisites
1. **ATEM Software Control**
   - The plugin requires Software Control

2. **Install StreamDeck Software**
   - Download and install StreamDeck software from Elgato
   - Ensure you're running version 6.4 or higher

### Plugin Installation
1. **Download the Plugin**
   - Clone this repository or download the latest release
   - Build the project using Visual Studio 2019 or later

2. **Install Dependencies**
   - Right-click the project and select "Manage NuGet Packages"
   - Restore all packages (StreamDeck-Tools and dependencies will be installed automatically)

3. **Build and Deploy**
   - Build the project in Release mode
   - Copy the generated `.sdPlugin` folder to your StreamDeck plugins directory:
     ```
     %APPDATA%\Elgato\StreamDeck\Plugins\
     ```

4. **Restart StreamDeck**
   - Restart the StreamDeck software to load the new plugin

## ⚙️ Configuration

### ATEM Connection Setup
1. **Get ATEM IP Address**
   - Find your ATEM's IP address (check your network settings or ATEM setup menu)
   - Default IP is often `192.168.1.101`

2. **Configure Plugin Actions**
   - Add any ATEM action to your StreamDeck
   - Open the Property Inspector
   - Enter your ATEM's IP address
   - Configure Mix Effect block (ME 1-4)
   - Set input IDs and tally preferences

### Available Actions
- **ATEM Cut**: Performs an immediate cut transition
- **ATEM Auto Transition**: Executes an auto transition with current settings
- **ATEM Set Next Transition**: Configure transition style (Mix, Dip, Wipe, DVE, Stinger) and duration
- **ATEM Preview**: Set preview input with tally support
- **ATEM Program**: Set program input with tally support

## 🎮 Usage

### Basic Operation
1. **Connect to ATEM**
   - The plugin will automatically attempt to connect to your ATEM switcher
   - Connection status is indicated by the button functionality and tally states

2. **Input Controls**
   - **Preview/Program Actions**: Press to set the configured input as Preview or Program
   - **Tally Indicators**: Buttons show GREEN when input is on Preview, RED when on Program

3. **Transition Controls**
   - **Cut**: Immediate cut between Preview and Program
   - **Auto**: Executes transition using current ATEM settings
   - **Set Next Transition**: Configure transition parameters before executing

### Troubleshooting
- **Connection Issues**: Verify ATEM IP address and network connectivity
- **No Tally Response**: Check if the input ID matches your ATEM configuration
- **Plugin Not Loading**: Check StreamDeck software version compatibility (6.4+)

## 🔧 Development

### Project Structure
```
StreamDeck-ATEM/
├── StreamDeck-ATEM/
│   ├── KeyAction.cs          # Main plugin action implementation
│   ├── DialsAction.cs        # Dial controls (experimental)
│   ├── KeyAndDialsAction.cs  # Combined key and dial actions
│   ├── manifest.json         # Plugin manifest
│   ├── PropertyInspector/    # UI configuration
│   └── Images/              # Plugin icons and images
├── packages/                # NuGet packages
└── StreamDeck-ATEM.sln     # Visual Studio solution
```

### Building from Source
1. **Clone the Repository**
   ```bash
   git clone https://github.com/yourusername/StreamDeck-ATEM.git
   cd StreamDeck-ATEM
   ```

2. **Open in Visual Studio**
   - Open `StreamDeck-ATEM.sln` in Visual Studio 2019 or later
   - Restore NuGet packages
   - Build the solution

3. **Configure ATEM SDK Path**
   - Update the `ATEMSDKPath` property in the project file
   - Ensure BMDSwitcherAPI.dll is accessible

### Dependencies
- **StreamDeck-Tools**: BarRaider's StreamDeck development framework
- **BMDSwitcherAPI**: Blackmagic Design ATEM SDK
- **Newtonsoft.Json**: JSON serialization
- **NLog**: Logging framework

## ⚠️ Important Notes

### Experimental Status
This plugin is currently in **experimental** status. Some features may not work as expected, and the API may change in future versions.

### Missing Features
- Advanced transition controls
- Input switching
- Audio controls
- Multi-viewer controls
- Recording/streaming controls

### Limitations
- Windows x64 only
- Requires ATEM Software Control to be running
- Limited to basic switcher operations
- Network connectivity required

## 🤝 Contributing

Contributions are welcome! Please feel free to submit issues, feature requests, or pull requests.

### Development Guidelines
1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly with ATEM hardware
5. Submit a pull request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- **BarRaider**: For the excellent StreamDeck-Tools framework
- **Blackmagic Design**: For the ATEM SDK and documentation
- **Elgato**: For the StreamDeck platform

## 📞 Support

For support and questions:
- **Issues**: Use the GitHub Issues page
- **Discussions**: Join the project discussions
- **Documentation**: Check the [Wiki](../../wiki) for detailed guides

---

**Note**: This plugin is not officially affiliated with Blackmagic Design or Elgato. Use at your own risk in production environments.
