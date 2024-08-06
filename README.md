# ProjectMarvin
![image](https://github.com/user-attachments/assets/51b1cb75-3ddf-4b65-be63-333e1d8f0707)

The point of this project is to

1. Give myself a tool when debugging my homebuilt Smart Home stuff - I use RPi Pico W with Micropython
2. Have a central monitor/logging point for all my devices and services. With an easy to call API endpoint, so low end devices can use it
3. A "framework" for API-functions and a WEB UI. Whenever I dream up a new idea, the building blocks are aldready in place making it simple and fast to add new stuff
4. Stepping stones/Builing blocks for other folks who is coding IoT stuff - and maybe are more into electronics than code
5. ... Always fun to build stuff with new technologies :-)

So what is this? It is a simple .NET 8 Blazor ServerApp project + ASP Net Core Minimal API project that uses SignalR/Websocket to display realtime incoming LogEntries. 

The solution is divided into two projects, one serversideproject Webproject, with identity enabled and one API project. 

The API:s included are:

1. LOG (GET) - simplest way to commit a new LogEntry to the monitor(webApp). The class used is "LogEntry" - modify it as you see fit. If you only send a single string as LogEntry, a new LogEntry will be created and the single string will be set as "Message"
2. LOG (POST) - same as above, but made for POST requests
3. ECHO (GET) - Send in something and get it Echoed back. I use this to check that WiFi is healty when using my Pico W:s
4. GetLocalTime (GET) - returns a DateTime string that can be used to set a RTC clock - or simply to get a Timestamp on simple IoT devices.

The API:s can be viewed in Swagger using URL: https://localhost:port/swagger

Included are also some client-test code, one micropython file showing how to use both the GET and POST version of LOG - and a .HTTP file that can be used for testing in VS2022 or VS Code. In the python file, don't forget to **set your own WiFi config!**

Requirements:

1. NET 8 SDK
2. VS 2022 Community or better / VS Code with C# Dev Kit
3. If you are going to call API:s - you must host BlazorLogWeb so it gets an IP number - not "localhost". If you only use it locally in your internal network, you can use http or https. If you want to host this webapp internet facing, you must add some kind of login and you must use HTTPS.
4. SQLite Database - can be run on all platforms

Coding stuff:
1. QuickGrid uses Scoped CSS to style the headers and lines. "Home.razor.css" - ::deep is for selection the QuickGrid scope. For this to work, the Quickgrid must be contained in a container, like a DIV tag
Have Fun!

// Lazze Ziden - Stockholm Sweden
