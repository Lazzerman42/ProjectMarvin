import network
import ujson
import socket
import ubinascii
import urequests as requests
from time import sleep

# URL to LOG API, In my case a Blazor Minimal API .Net v8
LOGURL="http://192.168.x.x:4200/Api/Log/" # Set your IP-adress
HOSTNAME="PythonMachine1" # Network hostname
TIMEOUTINSECONDS=2

#######################################################################
def SendLog(message, sender=HOSTNAME, logType="Info"):
    
    message = urlencode_string(message) # replaces non US ASCII chars with HTML codes
    
    # you can define the log like this
    status = {}
    status["LogType"] = "Info"
    status["Sender"] = HOSTNAME
    status["Message"] = message
   
    # or like this ( not used in the code now ) 
    mydata = {
    "logType" : logType,
    "Sender" : sender,
    "Message" : message
    }
   
    jsonTemp = ujson.dumps(status) # KVP to JSON String
    
    en = urlencode_string(jsonTemp) # HTML Encode
    
    # Use either GET or POST - here is an example of both
        # LOG via GET method - JSON as UrlEncoded string
       # response = requests.get(url=LOGURL+en,timeout=4)
       # response.close()
        
        # LOG via POST method - JSON with urlEncoded strings
    try:
        response = requests.post(LOGURL,headers={'content-type': 'application/json'},data=ujson.dumps(mydata),timeout=TIMEOUTINSECONDS)
        result = response.json()
        response.close()
    except Exception as e:
        print("Error")
        pass

########################################################
def urlencode_string(s):
    """
    URL Encode String

    Args:
        s (str): String to encode.

    Returns:
        str: A URL-encoded string.
    """
    safe_chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.-"
    encoded_chars = []
    for char in s:
        if char in safe_chars:
            encoded_chars.append(char)
        else:
            encoded_chars.append('%{:02X}'.format(ord(char)))
    return ''.join(encoded_chars)

########################################################
