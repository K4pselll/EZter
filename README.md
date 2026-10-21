## 1. What is EZter? (or simply: EZ) 
EZ is a fast, command workshop, and i mean it, you create your own commands there, then execute them in a normal terminal window, using ez [command name], it can be useful in some scenarios
EZ lets you create a command with C#, cmd command, python, and node, your choice! you can program it, or give it commands to execute, in order as you type it.
## 2. Setup
To setup EZ you need .NET 10.0 (required) and git
1. Clone the repo with git.
   ```bash
   git clone https://github.com/K4psell/EZter
   cd EZter
   ```
2. Build the program
   Build a standalone app that works on any machine
    * **Windows:** `dotnet publish -c Release -r win-x64 --self-contained true`
   * **Linux:** `dotnet publish -c Release -r linux-x64 --self-contained true`
   * **macOS:** `dotnet publish -c Release -r osx-x64 --self-contained true`
  Go to the `bin/Release/net10.0/[your-architecture]/publish/` Folder and run your app!
  
Or...
  Quick Setup, just build it. (works only on your machine)
  ```bash
  dotnet build
 ```
  Go to `EZter/bin/Debug/net10.0/Ezter.exe` Folder and run your app!

3. Add to PATH
   After opening the program, Look up on the window, there will be categories as file, edit, etc. for this step we need the Tools category.
   After clicking on Tools, click Add to PATH.
   Now you can create a test command, go to another, fresh command window and type EZ [your command name]

### **Hooray!** You just finished the setup for EZ, now go create commands, any that you want, 
if you want to publish your command, go to `/Commands`, find your command by name, it is supposted to be a .ezc file. then publish it, or send to a friend.

## 3. Command import
You got a command, from the internet, or from a friend, you dont know how to use it, its simple, go to the `/commands` folder and put the .ezc file there
**But before running** open the .ezc file in notepad, or in a terminal type ez (that opens the editor) and look what the command does, you cant trust everything.
