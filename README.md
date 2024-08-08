# ProjectMarvin
![image](https://github.com/user-attachments/assets/51b1cb75-3ddf-4b65-be63-333e1d8f0707)
*This is my usecase - I need to log/collect info from lots of different devices and computers.*

![image](https://github.com/user-attachments/assets/a22cdd5a-4260-4ec6-9077-599e77117867)
*This is the Blazor Server WebApp, logged in as Marvin@log.com*

![image](https://github.com/user-attachments/assets/a786a21d-4e2b-4603-a61c-4b63babecb4d)
*API project with swagger*

##The point of this project is to

1. Give myself a tool when debugging my homebuilt Smart Home stuff - I use RPi Pico W with Micropython
2. Have a central monitor/logging point for all my devices and services. With an easy to call API endpoint, so low end devices can use it
3. A "framework" for API-functions and a WEB UI. Whenever I dream up a new idea, the building blocks are aldready in place making it simple and fast to add new stuff
4. Stepping stones/Builing blocks for other folks who is coding IoT stuff - and maybe are more into electronics than code
5. ... Always fun to build stuff with new technologies :-)

So what is this? It is a simple .NET 8 Blazor ServerApp project + ASP Net Core Minimal API project that uses SignalR/Websocket to display realtime incoming LogEntries. 

The solution is divided into two projects, one serversideproject Webproject, with identity enabled and one API project. 

##The API:s included are:

1. LOG (GET) - simplest way to commit a new LogEntry to the monitor(webApp). The class used is "LogEntry" - modify it as you see fit. If you only send a single string as LogEntry, a new LogEntry will be created and the single string will be set as "Message"
2. LOG (POST) - same as above, but made for POST requests
3. ECHO (GET) - Send in something and get it Echoed back. I use this to check that WiFi is healty when using my Pico W:s
4. GetLocalTime (GET) - returns a DateTime string that can be used to set a RTC clock - or simply to get a Timestamp on simple IoT devices.

The API:s can be viewed in Swagger using URL: https://localhost:port/swagger

Included are also some client-test code, one micropython file showing how to use both the GET and POST version of LOG - and a .HTTP file that can be used for testing in VS2022 or VS Code. In the python file, don't forget to **set your own WiFi config!**

##Requirements:

1. NET 8 SDK
2. VS 2022 Community or better / VS Code with C# Dev Kit
3. If you are going to call API:s from other devices, - you must host Project Marvin API so it gets an IP number - not "localhost". If you only use it locally in your internal network
4. SQLite Database - can be run on all platforms

##Coding stuff:
1. QuickGrid uses Scoped CSS to style the headers and lines. "Home.razor.css" - ::deep is for selection the QuickGrid scope. For this to work, the Quickgrid must be contained in a container, like a DIV tag
2. API Project have an Attribute-class for checking "API KEY" - it is not used right now, but can easily be added to all endpoints
3. Blazor Server App - have some "smart" code to reconnect to SignalR that is run in the API project. A green or red symnbol is shown when connected/disconnected. The code for this is in the Home.razor.cs in the Project Marvin project
4. Identity is enabled - also using SQLite. You can add the [Authorize] keyword to all pages you want to require login. I'm having it on the Register page - so no one can register a new account without already being logged in.
5. IPFilterMiddleware is a class that can be registered in the API project, it will stop all API calls from non local IP-adresses.

##Ideas for the future
1. You can host the solultion in the cloud if you like
2. Or in containers/docker
3. Add support for MQTT, both sending and receiving
4. Add new Web-pages with information / stuff that makes sense for your project needs

##Getting started / Setup
1. The projects are configured to use localhost - API project at port 4200. If you run both web and API locally, you can use Kestrel for both. If you want to access the Marvin Blazor Web from internet, use HTTPS/SSL and host using IIS ( or whats best on your platform )
2. Check the AppSettings.json for the projects, the Path for SQLite LogDB is essential.
3. I run the Marvin API locally, using Kestrel - and I let it start on the current machines IP + port 4200, uncomment the code on line 20-24 in Program.cs (Marvin API) if you want to do the same
4. The user in the Identity database is: Marvin@log.com / Marvin42!
5. Make sure you change password / makes a new user before exposing the Web to Internet.

Have Fun!

// Lazze Ziden - Stockholm Sweden
