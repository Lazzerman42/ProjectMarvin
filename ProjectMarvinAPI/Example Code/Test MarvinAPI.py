import network
import ujson
import socket
import ubinascii
import urequests as requests
from time import sleep
from logapi import SendLog
######################################## Globals ###################################

SSID = 'YourSSID'
WIFIPWD = 'YourWiFiPassword'

wlan = network.WLAN(network.STA_IF)
wlan.active(True)

HOSTNAME="PythonMachine1" # Network hostname

########################################################
def connect():
    #Connect to WLAN
    network.hostname(HOSTNAME)    
    wlan.connect(SSID, WIFIPWD)

    while wlan.isconnected() == False:
        print('Waiting for connection...')
        sleep(2)
    print(wlan.ifconfig())
    network.hostname(HOSTNAME)
############################ MAIN ######################

try:
    connect() # WiFi
    SendLog("A Logmessage from mr Pico W") # Uses ISO8859-1 not UTF8    
except (TypeError) as err_obj:
    print(err_obj)

########################################################
