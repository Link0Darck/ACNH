#!/usr/bin/env bash

# -------------------------------------------------
# Script qui demarre le BOT ACNH
# -------------------------------------------------
SCREEN="ACNH"  # nom utilis� pour le screen
#chemin par default dotnet /usr/local/bin/dotnet et /usr/local/bin/dotnet
#COMMAND="dotnet run --framework net5.0"  # commande de lancement du serveur (sur un raspberry)
COMMAND="/usr/local/bin/dotnet SysBot.ACNHOrders.dll"  # commande de lancement du serveur (sur un raspberry)
#https://www.petecodes.co.uk/install-and-use-microsoft-dot-net-5-with-the-raspberry-pi/

# Cette ligne peut-�tre supprim�e si le bot
# et le script sont dans le m�me dossier :
cd ~/BOT-ACNH/bin/Release/net5.0; # emplacement du bot
# ------------------------------------------------

#export dotnet
export PATH=$PATH:$HOME/.dotnet/tools

#Temps d'attente
#sleep 1m;

echo " ";

echo "Lancement Du Bot";

#ACNH
screen -AmdS $SCREEN $COMMAND;

exit 0
