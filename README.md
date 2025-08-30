# StreamDeck ATEM Plugin

A powerful Elgato StreamDeck plugin that provides direct control over Blackmagic Design ATEM video switchers. This plugin allows you to control your ATEM switcher directly from your StreamDeck, making live production workflows more efficient and streamlined.

## 🎯 Features

### Current Features
- **ATEM Connection Management**: Automatic connection to ATEM switchers via IP address
- **Basic Switcher Controls**:
  - Cut transitions
  - Auto transitions
- **StreamDeck Integration**: Full integration with StreamDeck keypad and dial controls
- **Real-time Status**: Connection status monitoring and feedback

### Experimental Features
- **Dial Controls**: Basic dial rotation and press functionality (experimental)
- **Multi-action Support**: Framework for complex multi-button actions

## 📋 Requirements

### System Requirements
- **Operating System**: Windows 10 x64 or later
- **StreamDeck Software**: Version 6.4 or higher
- **ATEM Software Control**: Must be installed and running
- **Network**: ATEM switcher must be accessible via network

### Hardware Requirements
- **Elgato StreamDeck**: Any StreamDeck model (Standard, XL, Mini, or Plus)
- **ATEM Switcher**: Any Blackmagic Design ATEM model
- **Network Connection**: Wired or wireless connection to ATEM switcher

## 🚀 Installation

### Prerequisites
1. **Install ATEM Software Control**
   - Download and install the latest version from Blackmagic Design
   - Ensure your ATEM switcher is properly configured and accessible

2. **Install StreamDeck Software**
   - Download and install StreamDeck software from Elgato
   - Ensure you're running version 6.4 or higher

### Plugin Installation
1. **Download the Plugin**
   - Clone this repository or download the latest release
   - Build the project using Visual Studio 2019 or later

2. **Install Dependencies**
   - Right-click the project and select "Manage NuGet Packages"
   - Restore all packages (or install the latest StreamDeck-Tools from Nuget)

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
   - Open ATEM Software Control
   - Note the IP address of your ATEM switcher

2. **Configure Plugin**
   - Add the plugin to your StreamDeck
   - Open the Property Inspector
   - Enter your ATEM's IP address (default: `192.168.1.100`)
   - Select your desired action type

### Available Actions
- **Cut**: Performs an immediate cut transition
- **Auto Transition**: Executes an auto transition with current settings

## 🎮 Usage

### Basic Operation
1. **Connect to ATEM**
   - The plugin will automatically attempt to connect to your ATEM switcher
   - Connection status is indicated by the button state

2. **Execute Actions**
   - Press the configured StreamDeck button to execute the selected action
   - The plugin will send the command to your ATEM switcher

### Troubleshooting
- **Connection Issues**: Verify ATEM IP address and network connectivity
- **No Response**: Ensure ATEM Software Control is running
- **Plugin Not Loading**: Check StreamDeck software version compatibility

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
