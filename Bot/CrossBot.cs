using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NHSE.Core;
using NHSE.Villagers;
using ACNHMobileSpawner;
using SysBot.Base;
using System.Text;
using System.IO;
using System.Collections.Generic;

namespace SysBot.ACNHOrders
{
    public sealed class CrossBot : SwitchRoutineExecutor<CrossBotConfig>
    {
        private ConcurrentQueue<IACNHOrderNotifier<Item>> Orders => QueueHub.CurrentInstance.Orders;
        private uint InventoryOffset { get; set; } = (uint)OffsetHelper.InventoryOffset;

        public readonly ConcurrentQueue<ItemRequest> Injections = new();
        public readonly ConcurrentQueue<SpeakRequest> Speaks = new();
        public readonly ConcurrentQueue<VillagerRequest> VillagerInjections = new();
        public readonly ConcurrentQueue<MapOverrideRequest> MapOverrides = new();
        public readonly ConcurrentQueue<TurnipRequest> StonkRequests = new();
        public readonly PocketInjectorAsync PocketInjector;
        public readonly DodoPositionHelper DodoPosition;
        public readonly AnchorHelper Anchors;
        public readonly VisitorListHelper VisitorList;
        public readonly DummyOrder<Item> DummyRequest = new();
        public readonly ISwitchConnectionAsync SwitchConnection;
        public readonly ConcurrentBag<IDodoRestoreNotifier> DodoNotifiers = new();

        public readonly ExternalMapHelper ExternalMap;

        public readonly DropBotState State;

        public readonly DodoDraw? DodoImageDrawer;

        public MapTerrainLite Map { get; private set; } = new MapTerrainLite(new byte[MapGrid.MapTileCount32x32 * Item.SIZE]);
        public TimeBlock LastTimeState { get; private set; } = new();
        public bool CleanRequested { private get; set; }
        public bool RestoreRestartRequested { private get; set; }
        public string DodoCode { get; set; } = "Pas encore de code défini.";
        public string VisitorInfo { get; set; } = "Pas encore d'informations pour les visiteurs.";
        public string TownName { get; set; } = "Pas encore de nom de ville.";
        public string LastArrival { get; private set; } = string.Empty;
        public string LastArrivalIsland { get; private set; } = string.Empty;
        public ulong CurrentUserId { get; set; } = default!;
        public string CurrentUserName { get; set; } = string.Empty;
        public bool GameIsDirty { get; set; } = true; // Sale si crash ou dernier utilisateur n'est pas arrivé/parti correctement
        public ulong ChatAddress { get; set; } = 0;
        public DateTime LastDodoFetchTime { get; private set; } = DateTime.Now;

        public VillagerHelper Villagers { get; private set; } = VillagerHelper.Empty;

        public CrossBot(CrossBotConfig cfg) : base(cfg)
        {
            State = new DropBotState(cfg.DropConfig);
            Anchors = new AnchorHelper(Config.AnchorFilename);
            if (Connection is ISwitchConnectionAsync con)
                SwitchConnection = con;
            else
                throw new Exception("La connexion est nulle.");

            if (Connection is SwitchSocketAsync ssa)
                ssa.MaximumTransferSize = cfg.MapPullChunkSize;

            if (File.Exists("dodo.png") && File.Exists("dodo.ttf"))
                DodoImageDrawer = new DodoDraw(Config.DodoModeConfig.DodoFontPercentageSize);

            DodoPosition = new DodoPositionHelper(this);
            VisitorList = new VisitorListHelper(this);
            PocketInjector = new PocketInjectorAsync(SwitchConnection, InventoryOffset);

            var fileName = File.Exists(Config.DodoModeConfig.LoadedNHLFilename) ? File.ReadAllText(Config.DodoModeConfig.LoadedNHLFilename) + ".nhl" : string.Empty;
            ExternalMap = new ExternalMapHelper(cfg, fileName);
        }

        public override void SoftStop() => Config.AcceptingCommands = false;

        public override async Task MainLoop(CancellationToken token)
        {
            // Valider le vecteur de spawn de la carte
            if (Config.MapPlaceX < 0 || Config.MapPlaceX >= (MapGrid.AcreWidth * 32))
            {
                LogUtil.LogInfo($"{Config.MapPlaceX} n'est pas une valeur valide pour {nameof(Config.MapPlaceX)}. Exit !", Config.IP);
                return;
            }

            if (Config.MapPlaceY < 0 || Config.MapPlaceY >= (MapGrid.AcreHeight * 32))
            {
                LogUtil.LogInfo($"{Config.MapPlaceY} n'est pas une valeur valide pour {nameof(Config.MapPlaceY)}. Exit !", Config.IP);
                return;
            }

            // Déconnecte notre contrôleur virtuel ; se reconnectera une fois que nous aurons envoyé une commande de bouton après une requête.
            LogUtil.LogInfo("Detaching controller on startup as first interaction.", Config.IP);
            await Connection.SendAsync(SwitchCommand.DetachController(), token).ConfigureAwait(false);
            await Task.Delay(200, token).ConfigureAwait(false);

            // dessin
            await UpdateBlocker(false, token).ConfigureAwait(false);
            await SetScreenCheck(false, token).ConfigureAwait(false);

            // obtenir la version
            await Task.Delay(0_100, token).ConfigureAwait(false);
            LogUtil.LogInfo("Tentative d'obtention de la version. Veuillez patienter...", Config.IP);
            string version = await SwitchConnection.GetVersionAsync(token).ConfigureAwait(false);
            LogUtil.LogInfo($"La version de sys-botbase identifiée comme : {version}", Config.IP);

            // définir la taille de lecture infinie si elle est supérieure à 2.3
            if (float.TryParse(version, out var verFloat))
                if (verFloat >= 2.3)
                    SwitchConnection.MaximumTransferSize = int.MaxValue;

            // Obtenir le décalage de l'inventaire
            InventoryOffset = await this.GetCurrentPlayerOffset((uint)OffsetHelper.InventoryOffset, (uint)OffsetHelper.PlayerSize, token).ConfigureAwait(false);
            PocketInjector.WriteOffset = InventoryOffset;

            // Valider le décalage de l'inventaire.
            LogUtil.LogInfo("Vérification de la validité de la compensation d'inventaire.", Config.IP);
            var valid = await GetIsPlayerInventoryValid(InventoryOffset, token).ConfigureAwait(false);
            if (!valid)
            {
                LogUtil.LogInfo($"Inventaire lu à partir de {InventoryOffset} (0x{InventoryOffset:X8}) ne semble pas être valide.", Config.IP);
                if (Config.RequireValidInventoryMetadata)
                {
                    LogUtil.LogInfo("Exit!", Config.IP);
                    return;
                }
            }

            // Récupérer les éléments de carte et les données de terrain originaux et les stocker.
            LogUtil.LogInfo("Lecture du statut de la carte originale. Veuillez patienter...", Config.IP);
            var bytes = await Connection.ReadBytesAsync((uint)OffsetHelper.FieldItemStart, MapGrid.MapTileCount32x32 * Item.SIZE, token).ConfigureAwait(false);
            var bytesTerrain = await Connection.ReadBytesAsync((uint)OffsetHelper.LandMakingMapStart, MapTerrainLite.TerrainSize, token).ConfigureAwait(false);
            var bytesMapParams = await Connection.ReadBytesAsync((uint)OffsetHelper.OutsideFieldStart, MapTerrainLite.AcrePlusAdditionalParams, token).ConfigureAwait(false);
            Map = new MapTerrainLite(bytes, bytesTerrain, bytesMapParams)
            {
                SpawnX = Config.MapPlaceX,
                SpawnY = Config.MapPlaceY
            };

            // Tirer le nom de la ville et le stocker
            LogUtil.LogInfo("Nom de la ville de Reading. Veuillez attendre...", Config.IP);
            bytes = await Connection.ReadBytesAsync((uint)OffsetHelper.getTownNameAddress(InventoryOffset), 0x14, token).ConfigureAwait(false);
            TownName = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            VisitorList.SetTownName(TownName);
            LogUtil.LogInfo("Le nom de la ville est fixé à " + TownName, Config.IP);

            // extraire les données des villageois et les stocker
            Villagers = await VillagerHelper.GenerateHelper(this, token).ConfigureAwait(false);

            // extraire le temps de jeu et le stocker
            var timeBytes = await Connection.ReadBytesAsync((uint)OffsetHelper.TimeAddress, TimeBlock.SIZE, token).ConfigureAwait(false);
            LastTimeState = timeBytes.ToClass<TimeBlock>();
            LogUtil.LogInfo("Démarrage au moment du match: " + LastTimeState.ToString(), Config.IP);

            if (Config.ForceUpdateAnchors)
                LogUtil.LogInfo("Les ancres de mise à jour forcée sont définies sur true, aucune fonctionnalité ne sera activée.", Config.IP);

            LogUtil.LogInfo("Connexion réussie au robot. Démarrage de la boucle principale !", Config.IP);
            if (Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                if (Config.DodoModeConfig.FreezeMap)
                {
                    if (Config.DodoModeConfig.RefreshMap)
                    {
                        LogUtil.LogInfo("Vous ne pouvez pas geler et rafraîchir la carte en même temps. Choisissez l'un ou l'autre dans le fichier de configuration. Exit ...", Config.IP);
                        return;
                    }

                    LogUtil.LogInfo("Carte gelée, veuillez patienter...", Config.IP);
                    await SwitchConnection.FreezeValues((uint)OffsetHelper.FieldItemStart, Map.StartupBytes, ConnectionHelper.MapChunkCount, token).ConfigureAwait(false);
                }

                LogUtil.LogInfo("Les commandes ne sont pas acceptées en mode de restauration du dodo ! Veuillez vous assurer que tous les joy-cons et les contrôleurs sont arrimés ! Démarrage de la boucle de restauration du dodo...", Config.IP);
                while (!token.IsCancellationRequested)
                    await DodoRestoreLoop(false, token).ConfigureAwait(false);
            }

            while (!token.IsCancellationRequested)
                await OrderLoop(token).ConfigureAwait(false);
        }

        private async Task DodoRestoreLoop(bool immediateRestart, CancellationToken token)
        {
            await EnsureAnchorsAreInitialised(token);
            await VisitorList.UpdateNames(token).ConfigureAwait(false);
            if (File.Exists(Config.DodoModeConfig.LoadedNHLFilename))
                await AttemptEchoHook($"[Redémarré] {TownName} a été chargé en dernier lieu avec la couche : {File.ReadAllText(Config.DodoModeConfig.LoadedNHLFilename)}.nhl", Config.DodoModeConfig.EchoIslandUpdateChannels, token, true).ConfigureAwait(false);

            bool hardCrash = immediateRestart;
            if (!immediateRestart)
            {
                byte[] bytes = await Connection.ReadBytesAsync((uint)OffsetHelper.DodoAddress, 0x5, token).ConfigureAwait(false);
                DodoCode = Encoding.UTF8.GetString(bytes, 0, 5);

                if (DodoPosition.IsDodoValid(DodoCode) && Config.DodoModeConfig.EchoDodoChannels.Count > 0)
                    await AttemptEchoHook($"[{DateTime.Now:yyyy-MM-dd hh:mm:ss tt}] Le code Dodo pour {TownName} a été mis à jour, le nouveau code Dodo est : {DodoCode}.", Config.DodoModeConfig.EchoDodoChannels, token).ConfigureAwait(false);

                NotifyDodo(DodoCode);

                await SaveDodoCodeToFile(token).ConfigureAwait(false);

                while (await IsNetworkSessionActive(token).ConfigureAwait(false))
                {
                    await Task.Delay(2_000, token).ConfigureAwait(false);

                    if (RestoreRestartRequested)
                    {
                        RestoreRestartRequested = false;
                        await AttemptEchoHook($"[{DateTime.Now:yyyy-MM-dd hh:mm:ss tt}] Veuillez attendre le nouveau code dodo pour {TownName}.", Config.DodoModeConfig.EchoDodoChannels, token).ConfigureAwait(false);
                        await DodoRestoreLoop(true, token).ConfigureAwait(false);
                        return;
                    }

                    NotifyState(GameState.Active);

                    var owState = await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false);
                    if (Config.DodoModeConfig.RefreshMap)
                        if (owState == OverworldState.UserArriveLeaving || owState == OverworldState.Loading) // rafraîchir uniquement lorsque quelqu'un quitte ou rejoint un bâtiment ou lorsqu'il en sort ou en entretient un.
                            await ClearMapAndSpawnInternally(null, Map, Config.DodoModeConfig.RefreshTerrainData, token).ConfigureAwait(false);

                    if (Config.DodoModeConfig.MashB)
                        for (int i = 0; i < 5; ++i)
                            await Click(SwitchButton.B, 0_200, token).ConfigureAwait(false);

                    var timeBytes = await Connection.ReadBytesAsync((uint)OffsetHelper.TimeAddress, TimeBlock.SIZE, token).ConfigureAwait(false);
                    LastTimeState = timeBytes.ToClass<TimeBlock>();

                    await DropLoop(token).ConfigureAwait(false);

                    var diffs = await VisitorList.UpdateNames(token).ConfigureAwait(false);

                    if (Config.DodoModeConfig.EchoArrivalChannels.Count > 0)
                        foreach (var diff in diffs)
                            if (!diff.Arrived)
                                await AttemptEchoHook($"> [{DateTime.Now:yyyy-MM-dd hh:mm:ss tt}] 🛫 {diff.Name} a quitté {TownName}", Config.DodoModeConfig.EchoArrivalChannels, token).ConfigureAwait(false);

                    // Vérifier les nouveaux arrivants
                    if (await IsArriverNew(token).ConfigureAwait(false))
                    {
                        if (Config.DodoModeConfig.EchoArrivalChannels.Count > 0)
                            await AttemptEchoHook($"> [{DateTime.Now:yyyy-MM-dd hh:mm:ss tt}] 🛬 {LastArrival} de {LastArrivalIsland} se joint à {TownName}.{(Config.DodoModeConfig.PostDodoCodeWithNewArrivals ? $" Le code Dodo est: {DodoCode}." : string.Empty)}", Config.DodoModeConfig.EchoArrivalChannels, token).ConfigureAwait(false);

                        var nid = await Connection.ReadBytesAsync((uint)OffsetHelper.ArriverNID, 8, token).ConfigureAwait(false);
                        var islandId = await Connection.ReadBytesAsync((uint)OffsetHelper.ArriverVillageId, 4, token).ConfigureAwait(false);
                        bool IsSafeNewAbuse = true;
                        try
                        {
                            var newnid = BitConverter.ToUInt64(nid, 0);
                            var newnislid = BitConverter.ToUInt32(islandId, 0);
                            var plaintext = $"Arrivée sur l'île au trésor";
                            IsSafeNewAbuse = NewAntiAbuse.Instance.LogUser(newnislid, newnid, string.Empty, plaintext);
                            LogUtil.LogInfo($"Arrivée enregistrée: NID={newnid} TownID={newnislid} Order details={plaintext}", Config.IP);

                            if (!IsSafeNewAbuse)
                                LogUtil.LogInfo((Globals.Bot.Config.OrderConfig.PingOnAbuseDetection ? $"Pinging <@{Globals.Self.Owner}>: " : string.Empty) + $"{LastArrival} (NID: {newnid}) est dans la liste des abuseurs connus. Il est probable que cet utilisateur abuse de votre île au trésor.", Globals.Bot.Config.IP);
                        }
                        catch { }

                        await Task.Delay(60_000, token).ConfigureAwait(false);

                        // Effacer le nom d'utilisateur de la dernière arrivée
                        await Connection.WriteBytesAsync(new byte[0x14], (uint)OffsetHelper.ArriverNameLocAddress, token).ConfigureAwait(false);
                        LastArrival = string.Empty;
                    }

                    await SaveVisitorsToFile(token).ConfigureAwait(false);

                    await DropLoop(token).ConfigureAwait(false);

                    if (VillagerInjections.TryDequeue(out var vil))
                        await Villagers.InjectVillager(vil, token).ConfigureAwait(false);
                    var lostVillagers = await Villagers.UpdateVillagers(token).ConfigureAwait(false);
                    if (Config.DodoModeConfig.ReinjectMovedOutVillagers) // réinjecter les villageois perdus si nécessaire
                        if (lostVillagers != null)
                            foreach (var lv in lostVillagers)
                                if (!lv.Value.StartsWith("non"))
                                    VillagerInjections.Enqueue(new VillagerRequest("REINJECT", VillagerResources.GetVillager(lv.Value), (byte)lv.Key, GameInfo.Strings.GetVillager(lv.Value)));
                    
                    await SaveVillagersToFile(token).ConfigureAwait(false);

                    MapOverrideRequest? mapRequest;
                    if ((MapOverrides.TryDequeue(out mapRequest) || ExternalMap.CheckForCycle(out mapRequest)) && mapRequest != null)
                    {
                        var tempMap = new MapTerrainLite(mapRequest.Item, Map.StartupTerrain, Map.StartupAcreParams)
                        {
                            SpawnX = Config.MapPlaceX,
                            SpawnY = Config.MapPlaceY
                        };
                        Map = tempMap;

                        // Rédigez une carte complète avec la NHL ou le freeze nouvellement chargés.
                        if (!Config.DodoModeConfig.FreezeMap)
                            await ClearMapAndSpawnInternally(null, Map, Config.DodoModeConfig.RefreshTerrainData, token, true).ConfigureAwait(false);
                        else
                            await SwitchConnection.FreezeValues((uint)OffsetHelper.FieldItemStart, Map.StartupBytes, ConnectionHelper.MapChunkCount, token).ConfigureAwait(false);

                        await AttemptEchoHook($"{TownName} est passé à la couche d'articles: {mapRequest.OverrideLayerName}", Config.DodoModeConfig.EchoIslandUpdateChannels, token).ConfigureAwait(false);
                        await SaveLayerNameToFile(Path.GetFileNameWithoutExtension(mapRequest.OverrideLayerName), token).ConfigureAwait(false); 
                    }

                    if (Config.DodoModeConfig.AutoNewDodoTimeMinutes > -1)
                        if ((DateTime.Now - LastDodoFetchTime).TotalMinutes >= Config.DodoModeConfig.AutoNewDodoTimeMinutes && VisitorList.VisitorCount == 1) // 1 pour l'hôte
                            RestoreRestartRequested = true;
                }

                if (Config.DodoModeConfig.EchoDodoChannels.Count > 0)
                    await AttemptEchoHook($"[{DateTime.Now:yyyy-MM-dd hh:mm:ss tt}] Crash détecté sur {TownName}. Veuillez patienter pendant que j'obtiens un nouveau code Dodo.", Config.DodoModeConfig.EchoDodoChannels, token).ConfigureAwait(false);
                NotifyState(GameState.Fetching);
                LogUtil.LogInfo($"Crash détecté sur {TownName}, en attendant qu'overworld aille chercher le nouveau dodo.", Config.IP);
                await ResetFiles(token).ConfigureAwait(false);
                await Task.Delay(5_000, token).ConfigureAwait(false);

                // Effacer le code dodo
                await Connection.WriteBytesAsync(new byte[5], (uint)OffsetHelper.DodoAddress, token).ConfigureAwait(false);

                var startTime = DateTime.Now;
                // Attendre l'overworld
                LogUtil.LogInfo($"Commencer la boucle d'attente d'overworld.", Config.IP);
                while (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false) != OverworldState.Overworld)
                {
                    await Task.Delay(1_000, token).ConfigureAwait(false);
                    await Click(SwitchButton.B, 0_100, token).ConfigureAwait(false);
                    if (Math.Abs((DateTime.Now - startTime).TotalSeconds) > 45)
                    {
                        LogUtil.LogError($"Crash dur détecté sur {TownName}, redémarrage du jeu.", Config.IP);
                        hardCrash = true;
                        break;
                    }
                }
                LogUtil.LogInfo($"Fin de la boucle d'attente d'overworld.", Config.IP);
            }

            var result = await ExecuteOrderStart(DummyRequest, true, hardCrash, token).ConfigureAwait(false);

            if (result != OrderResult.Success)
            {
                LogUtil.LogError($"La restauration de Dodo a échoué avec une erreur : {result}. Redémarrage du jeu...", Config.IP);
                await DodoRestoreLoop(true, token).ConfigureAwait(false);
                return;
            }

            await SaveDodoCodeToFile(token).ConfigureAwait(false);
            LogUtil.LogError($"Restauration du dodo réussie. Nouveau dodo pour {TownName} est {DodoCode} et enregistré dans {Config.DodoModeConfig.DodoRestoreFilename}.", Config.IP);
            if (Config.DodoModeConfig.RefreshMap) // carte propre
                await ClearMapAndSpawnInternally(null, Map, Config.DodoModeConfig.RefreshTerrainData, token, true).ConfigureAwait(false);
        }

        // hacké dans le transfert discord, devrait vraiment être un délégué ou un transfert réutilisable
        private async Task AttemptEchoHook(string message, IReadOnlyCollection<ulong> channels, CancellationToken token, bool checkForDoublePosts = false)
        {
            foreach (var msgChannel in channels)
                if (!await Globals.Self.TrySpeakMessage(msgChannel, message, checkForDoublePosts).ConfigureAwait(false))
                    LogUtil.LogError($"Impossible de poster dans les canaux: {msgChannel}.", Config.IP);
        }

        private async Task OrderLoop(CancellationToken token)
        {
            if (!Config.AcceptingCommands)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false);
                return;
            }

            await EnsureAnchorsAreInitialised(token);

            if (Orders.TryDequeue(out var item) && !item.SkipRequested)
            {
                var result = await ExecuteOrder(item, token).ConfigureAwait(false);
                
                // Nettoyage
                LogUtil.LogInfo($"Sortie de la commande avec résultat : {result}", Config.IP);
                CurrentUserId = default!;
                LastArrival = string.Empty;
                CurrentUserName = string.Empty;
            }

            var timeBytes = await Connection.ReadBytesAsync((uint)OffsetHelper.TimeAddress, TimeBlock.SIZE, token).ConfigureAwait(false);
            var newTimeState = timeBytes.ToClass<TimeBlock>();
            if (LastTimeState.Hour < 5 && newTimeState.Hour == 5)
                GameIsDirty = true;
            LastTimeState = newTimeState;

            await Task.Delay(1_000, token).ConfigureAwait(false);
        }

        private async Task<OrderResult> ExecuteOrder(IACNHOrderNotifier<Item> order, CancellationToken token)
        {
            var idToken = Globals.Bot.Config.OrderConfig.ShowIDs ? $" (ID {order.OrderID})" : string.Empty;
            string startMsg = $"Ordre de départ pour: {order.VillagerName}{idToken}. Q Size: {Orders.ToArray().Length + 1}.";
            LogUtil.LogInfo($"{startMsg} ({order.UserGuid})", Config.IP);
            if (order.VillagerName != string.Empty && Config.OrderConfig.EchoArrivingLeavingChannels.Count > 0)
                await AttemptEchoHook($"> {startMsg}", Config.OrderConfig.EchoArrivingLeavingChannels, token).ConfigureAwait(false);
            CurrentUserName = order.VillagerName;

            // Effacez toutes les injections persistantes du dernier utilisateur.
            Injections.ClearQueue();
            Speaks.ClearQueue();

            int timeOut = (Config.OrderConfig.UserTimeAllowed + 360) * 1_000; // 360 secondes = 6 minutes
            var cts = new CancellationTokenSource(timeOut);
            var cToken = cts.Token; // les jetons doivent être combinés, d'une manière ou d'une autre et éventuellement
            OrderResult result = OrderResult.Faulted;
            var orderTask = GameIsDirty ? ExecuteOrderStart(order, false, true, cToken) : ExecuteOrderMidway(order, cToken);
            try
            {
                result = await orderTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException e)
            {
                LogUtil.LogInfo($"{order.VillagerName} ({order.UserGuid}) a eu son temps mort de commande: {e.Message}.", Config.IP);
                order.OrderCancelled(this, "Malheureusement, un crash du jeu s'est produit alors que votre commande était en cours. Désolé, votre demande a été supprimée.", true);
            }

            if (result == OrderResult.Success)
            {
                GameIsDirty = await CloseGate(token).ConfigureAwait(false);
            }
            else
            {
                await EndSession(token).ConfigureAwait(false);
                GameIsDirty = true;
            }

            if (result == OrderResult.NoArrival || result == OrderResult.NoLeave)
                GlobalBan.Penalize(order.UserGuid.ToString());

            // Effacer le nom d'utilisateur de la dernière arrivée
            await Connection.WriteBytesAsync(new byte[0x14], (uint)OffsetHelper.ArriverNameLocAddress, token).ConfigureAwait(false);

            return result;
        }

        // exécuter un ordre directement après l'ordre de quelqu'un d'autre
        private async Task<OrderResult> ExecuteOrderMidway(IACNHOrderNotifier<Item> order, CancellationToken token)
        {
            while (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false) != OverworldState.Overworld)
                await Task.Delay(1_000, token).ConfigureAwait(false);

            order.OrderInitializing(this, string.Empty);

            // Configurer l'ordre localement, effacer la carte en tirant tout et en vérifiant la différence. La lecture est beaucoup plus rapide que l'écriture
            await ClearMapAndSpawnInternally(order.Order, Map, false, token).ConfigureAwait(false);

            // commande d'injection
            await InjectOrder(Map, token).ConfigureAwait(false);
            if (order.VillagerOrder != null)
                await Villagers.InjectVillager(order.VillagerOrder, token).ConfigureAwait(false);

            // Téléportation à Orville, nous devrions déjà y être mais les déconnexions obligent le joueur à faire demi-tour (deux fois, au cas où nous serions tirés en arrière).
            await SendAnchorBytes(3, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SendAnchorBytes(3, token).ConfigureAwait(false);

            return await FetchDodoAndAwaitOrder(order, false, token).ConfigureAwait(false);
        }

        // exécuter l'ordre à partir de zéro (appuyer sur home, arrêter le jeu, recommencer, généralement à cause de "une erreur de connexion s'est produite")
        private async Task<OrderResult> ExecuteOrderStart(IACNHOrderNotifier<Item> order, bool ignoreInjection, bool fromRestart, CancellationToken token)
        {
            // Méthode :
            // 1) Redémarrer le jeu. C'est la façon la plus fiable de le faire si le jeu tourne sans fin. Les décalages du code Dodo sont bizarres et n'ont pas de bons pointeurs.
            // 2) Attendez le discours d'Isabelle (s'il y en a un), notifiez au joueur d'être prêt, téléportez le joueur dans son aéroport puis devant Orville, ouvrez la porte et informez le dodo code.
            // 3) Aviser le joueur de venir maintenant, se téléporter à l'extérieur dans la zone de largage, attendre la commande de largage dans leur DM, le temps de la configuration ou jusqu'à ce que le joueur parte.
            // 4) Une fois le temps écoulé ou le départ du joueur, recommencez avec le prochain utilisateur.

            await Connection.SendAsync(SwitchCommand.DetachController(), token).ConfigureAwait(false);
            await Task.Delay(200, token).ConfigureAwait(false);

            if (fromRestart)
            {
                await RestartGame(token).ConfigureAwait(false);

                // Réinitialiser les bâtons
                await SetStick(SwitchStick.LEFT, 0, 0, 0_500, token).ConfigureAwait(false);

                // Configurer l'ordre localement, effacer la carte en tirant sur tout et en vérifiant la différence. La lecture est beaucoup plus rapide que l'écriture
                if (!ignoreInjection)
                {
                    await ClearMapAndSpawnInternally(order.Order, Map, false, token).ConfigureAwait(false);

                    if (order.VillagerOrder != null)
                        await Villagers.InjectVillager(order.VillagerOrder, token).ConfigureAwait(false);
                }

                // Appuyez sur A sur l'écran titre
                await Click(SwitchButton.A, 0_500, token).ConfigureAwait(false);

                // Attendre le temps de chargement qui semble être une éternité.
                // Attendre que le jeu nous téléporte de la position "enfer" à notre porte d'entrée. Continue d'appuyer sur A et B au cas où nous serions bloqués à l'intro du jour.
                int echoCount = 0;
                bool gameStarted = await EnsureAnchorMatches(0, 150_000, async () =>
                {
                    await ClickConversation(SwitchButton.A, 0_300, token).ConfigureAwait(false);
                    await ClickConversation(SwitchButton.B, 0_300, token).ConfigureAwait(false);
                    if (echoCount < 5)
                    {
                        if (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false) == OverworldState.Overworld)
                        {
                            LogUtil.LogInfo("J'ai atteint l'overworld, j'attends que l'ancre 0 corresponde...", Config.IP);
                            echoCount++;
                        }
                    }

                }, token);

                if (!gameStarted)
                {
                    var error = "Impossible d'atteindre l'overworld.";
                    LogUtil.LogError($"{error} Essai de la demande suivante.", Config.IP);
                    order.OrderCancelled(this, $"{error} Désolé, votre demande a été supprimée.", true);
                    return OrderResult.Faulted;
                }

                LogUtil.LogInfo("Ancre 0 appariée avec succès.", Config.IP);

                // commande d'injection
                if (!ignoreInjection)
                {
                    await InjectOrder(Map, token).ConfigureAwait(false);
                    order.OrderInitializing(this, string.Empty);
                }
            }

            while (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false) != OverworldState.Overworld)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                if (ignoreInjection)
                    await Click(SwitchButton.B, 0_500, token).ConfigureAwait(false);
            }

            // Délai pour l'animation
            await Task.Delay(1_800, token).ConfigureAwait(false);
            // Détachement de tout élément retenu
            await Click(SwitchButton.DDOWN, 0_300, token).ConfigureAwait(false);

            LogUtil.LogInfo($"Atteindre l'overworld, se téléporter à l'aéroport.", Config.IP);

            // Injecter l'ancre d'entrée de l'aéroport
            await SendAnchorBytes(2, token).ConfigureAwait(false);

            if (ignoreInjection)
            {
                await SendAnchorBytes(1, token).ConfigureAwait(false);
                LogUtil.LogInfo($"Vérification de l'annonce du matin", Config.IP);
                // Nous devons vérifier l'annonce matinale d'Isabelle.
                for (int i = 0; i < 3; ++i)
                    await Click(SwitchButton.B, 0_400, token).ConfigureAwait(false);
                while (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false) != OverworldState.Overworld)
                {
                    await Click(SwitchButton.B, 0_300, token).ConfigureAwait(false);
                    await Task.Delay(1_000, token).ConfigureAwait(false);
                }
            }

            // Sortez de tous les appels, événements, etc.
            bool atAirport = await EnsureAnchorMatches(2, 10_000, async () =>
            {
                await Click(SwitchButton.A, 0_300, token).ConfigureAwait(false);
                await Click(SwitchButton.B, 0_300, token).ConfigureAwait(false);
                await SendAnchorBytes(2, token).ConfigureAwait(false);
            }, token);

            await Task.Delay(0_500, token).ConfigureAwait(false);

            LogUtil.LogInfo($"Entrée dans l'aéroport.", Config.IP);

            await EnterAirport(token).ConfigureAwait(false);

            if (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false) == OverworldState.Null)
                return OrderResult.Faulted; // nous sommes dans l'eau

            // Téléportation à Orville (deux fois, au cas où nous serions tirés en arrière)
            await SendAnchorBytes(3, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            int numChecks = 10;
            LogUtil.LogInfo($"Tentative de distorsion vers le compteur de dodo...", Config.IP);
            while (!AnchorHelper.DoAnchorsMatch(await ReadAnchor(token).ConfigureAwait(false), Anchors.Anchors[3]))
            {
                await SendAnchorBytes(3, token).ConfigureAwait(false);
                if (numChecks-- < 0)
                    return OrderResult.Faulted;
                
                await Task.Delay(0_500, token).ConfigureAwait(false);
            }

            return await FetchDodoAndAwaitOrder(order, ignoreInjection, token).ConfigureAwait(false);
        }

        private async Task ClearMapAndSpawnInternally(Item[]? order, MapTerrainLite clearMap, bool includeAdditionalParams, CancellationToken token, bool forceFullWrite = false)
        {
            if (order != null)
            {
                clearMap.Spawn(MultiItem.DeepDuplicateItem(Item.NO_ITEM, 40)); // zone claire
                clearMap.Spawn(order);
            }

            await Task.Delay(2_000, token).ConfigureAwait(false);
            if (order != null)
                LogUtil.LogInfo("Le nettoyage de la carte a commencé.", Config.IP);
            if (forceFullWrite)
                await Connection.WriteBytesAsync(clearMap.StartupBytes, (uint)OffsetHelper.FieldItemStart, token).ConfigureAwait(false);
            else
            {
                var mapData = await Connection.ReadBytesAsync((uint)OffsetHelper.FieldItemStart, MapTerrainLite.ByteSize, token).ConfigureAwait(false);
                var offData = clearMap.GetDifferencePrioritizeStartup(mapData, Config.MapPullChunkSize, Config.DodoModeConfig.LimitedDodoRestoreOnlyMode && Config.AllowDrop, (uint)OffsetHelper.FieldItemStart);
                for (int i = 0; i < offData.Length; ++i)
                    await Connection.WriteBytesAsync(offData[i].ToSend, offData[i].Offset, token).ConfigureAwait(false);
            }

            if (includeAdditionalParams)
            {
                await Connection.WriteBytesAsync(clearMap.StartupTerrain, (uint)OffsetHelper.LandMakingMapStart, token).ConfigureAwait(false);
                await Connection.WriteBytesAsync(clearMap.StartupAcreParams, (uint)OffsetHelper.OutsideFieldStart, token).ConfigureAwait(false);
            }

            if (order != null)
                LogUtil.LogInfo("Le nettoyage de la carte est terminé.", Config.IP);
        }

        private async Task<OrderResult> FetchDodoAndAwaitOrder(IACNHOrderNotifier<Item> order, bool ignoreInjection, CancellationToken token)
        {
            LogUtil.LogInfo($"Parler à Orville. J'essaie d'obtenir le code Dodo pour {TownName}.", Config.IP);
            if (ignoreInjection)
                await SetScreenCheck(true, token).ConfigureAwait(false);
            await DodoPosition.GetDodoCode((uint)OffsetHelper.DodoAddress, false, token).ConfigureAwait(false);

            // essayez à nouveau si nous n'avons pas réussi à obtenir un dodo
            if (Config.OrderConfig.RetryFetchDodoOnFail && !DodoPosition.IsDodoValid(DodoPosition.DodoCode))
            {
                LogUtil.LogInfo($"Impossible d'obtenir un code Dodo valide pour {TownName}. Essayer à nouveau...", Config.IP);
                for (int i = 0; i < 10; ++i)
                    await ClickConversation(SwitchButton.B, 0_600, token).ConfigureAwait(false);
                await DodoPosition.GetDodoCode((uint)OffsetHelper.DodoAddress, true, token).ConfigureAwait(false);
            }

            await SetScreenCheck(false, token).ConfigureAwait(false);

            if (!DodoPosition.IsDodoValid(DodoPosition.DodoCode))
            {
                var error = "Échec de la connexion à Internet et de l'obtention d'un code Dodo.";
                LogUtil.LogError($"{error} Essai de la demande suivante.", Config.IP);
                order.OrderCancelled(this, $"Une erreur de connexion s'est produite : {error} Désolé, votre demande a été supprimée.", true);
                return OrderResult.Faulted;
            }

            DodoCode = DodoPosition.DodoCode;
            LastDodoFetchTime = DateTime.Now;

            if (!ignoreInjection)
                order.OrderReady(this, $"Vous avez {(int)(Config.OrderConfig.WaitForArriverTime * 0.9f)} secondes pour arriver. Le nom de mon île est **{TownName}**", DodoCode);

            if (DodoImageDrawer != null)
                DodoImageDrawer.Draw(DodoCode);

            // Téléportation vers la zone de sortie de l'aéroport (deux fois, au cas où nous serions rappelés).
            await SendAnchorBytes(4, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SendAnchorBytes(4, token).ConfigureAwait(false);

            // Sortez
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SetStick(SwitchStick.LEFT, 0, -20_000, 1_500, token).ConfigureAwait(false);
            await Task.Delay(1_000, token).ConfigureAwait(false);
            await SetStick(SwitchStick.LEFT, 0, 0, 1_500, token).ConfigureAwait(false);

            while (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false) != OverworldState.Overworld)
                await Task.Delay(1_000, token).ConfigureAwait(false);

            // Délai pour l'animation
            await Task.Delay(1_200, token).ConfigureAwait(false);

            while (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false) != OverworldState.Overworld)
                await Task.Delay(1_000, token).ConfigureAwait(false);

            // Téléportation vers la zone de largage (deux fois, au cas où nous serions repoussés).
            await SendAnchorBytes(1, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SendAnchorBytes(1, token).ConfigureAwait(false);

            if (ignoreInjection)
                return OrderResult.Success;

            LogUtil.LogInfo($"En attendant l'arrivée.", Config.IP);
            var startTime = DateTime.Now;
            // Attendre l'arrivée
            while (!await IsArriverNew(token).ConfigureAwait(false))
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                if (Math.Abs((DateTime.Now - startTime).TotalSeconds) > Config.OrderConfig.WaitForArriverTime)
                {
                    var error = "Le visiteur n'est pas arrivé.";
                    LogUtil.LogError($"{error}. Suppression de la file d'attente, passage à la commande suivante.", Config.IP);
                    order.OrderCancelled(this, $"{error} Votre demande a été supprimée.", false);
                    return OrderResult.NoArrival;
                }
            }

            var nid = await Connection.ReadBytesAsync((uint)OffsetHelper.ArriverNID, 8, token).ConfigureAwait(false);
            var islandId = await Connection.ReadBytesAsync((uint)OffsetHelper.ArriverVillageId, 4, token).ConfigureAwait(false);

            bool IsSafeNewAbuse = true;
            try
            {
                var newnid = BitConverter.ToUInt64(nid, 0);
                var newnislid = BitConverter.ToUInt32(islandId, 0);
                var plaintext = $"Nom et ID: {order.VillagerName}-{order.UserGuid}, Nom du villageois et ville: {LastArrival}-{LastArrivalIsland}";
                IsSafeNewAbuse = NewAntiAbuse.Instance.LogUser(newnislid, newnid, order.UserGuid.ToString(), plaintext);
                LogUtil.LogInfo($"Arrivée enregistrée: NID={newnid} TownID={newnislid} Order details={plaintext}", Config.IP);
            }
            catch(Exception e) 
            {
                LogUtil.LogInfo(e.Message + "\r\n" + e.StackTrace, Config.IP);
            }

            // Vérifier l'utilisateur par rapport aux abuseurs connus
            var IsSafe = LegacyAntiAbuse.CurrentInstance.LogUser(LastArrival, LastArrivalIsland, $"{order.VillagerName}-{order.UserGuid}") && IsSafeNewAbuse;
            if (!IsSafe)
            {
                if (!Config.AllowKnownAbusers)
                {
                    LogUtil.LogInfo($"{LastArrival} de {LastArrivalIsland} est un abuseur connu. A partir de la prochaine commande...", Config.IP);
                    order.OrderCancelled(this, $"Vous abusez des règles. Vous ne pouvez pas utiliser ce bot merci d'ouvrir un ticket.", false);
                    return OrderResult.NoArrival;
                }
                else
                {
                    LogUtil.LogInfo($"{LastArrival} de {LastArrivalIsland} est un abuseur connu, mais vous l'autorisez à utiliser votre robot à vos risques et périls.", Config.IP);
                }
            }

            order.SendNotification(this, $"Arrivée des visiteurs : {LastArrival}. Vos articles seront devant vous une fois que vous aurez atterri.");
            if (order.VillagerName != string.Empty && Config.OrderConfig.EchoArrivingLeavingChannels.Count > 0)
                await AttemptEchoHook($"> Arrivée des visiteurs : {order.VillagerName}", Config.OrderConfig.EchoArrivingLeavingChannels, token).ConfigureAwait(false);

            // Animation d'attente d'arrivée (embarquement, arrivée par la porte d'embarquement, terrible blague de l'hydravion dodo, etc.)
            await Task.Delay(10_000, token).ConfigureAwait(false);

            OverworldState state = OverworldState.Unknown;
            bool isUserArriveLeaving = false;
            // S'assurer que nous sommes sur le monde extérieur avant de commencer la boucle de minuterie/dépôt.
            while (state != OverworldState.Overworld)
            {
                state = await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false);
                await Task.Delay(0_500, token).ConfigureAwait(false);
                await Click(SwitchButton.A, 0_500, token).ConfigureAwait(false);

                if (!isUserArriveLeaving && state == OverworldState.UserArriveLeaving)
                {
                    await UpdateBlocker(true, token).ConfigureAwait(false);
                    isUserArriveLeaving = true;
                }
                else if (isUserArriveLeaving && state != OverworldState.UserArriveLeaving)
                {
                    await UpdateBlocker(false, token).ConfigureAwait(false);
                    isUserArriveLeaving = false;
                }

                await VisitorList.UpdateNames(token).ConfigureAwait(false);
                if (VisitorList.VisitorCount < 2)
                    break;
            }

            await UpdateBlocker(false, token).ConfigureAwait(false);

            // Mettre à jour l'identité de l'utilisateur actuel afin qu'il puisse utiliser les commandes de dépôt.
            CurrentUserId = order.UserGuid;

            // Nous vérifions si l'utilisateur est parti en contrôlant si quelqu'un a atteint ou non l'état Arrive/Leaving.
            startTime = DateTime.Now;
            bool warned = false;
            while (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false) != OverworldState.UserArriveLeaving)
            {
                await DropLoop(token).ConfigureAwait(false);
                await Click(SwitchButton.B, 0_300, token).ConfigureAwait(false);
                await Task.Delay(1_000, token).ConfigureAwait(false);
                if (Math.Abs((DateTime.Now - startTime).TotalSeconds) > (Config.OrderConfig.UserTimeAllowed - 60) && !warned)
                {
                    order.SendNotification(this, "Il vous reste 60 secondes avant que je ne passe à la commande suivante. Veuillez vous assurer que vous pouvez récupérer vos articles et partir dans ce délai..");
                    warned = true;
                }

                if (Math.Abs((DateTime.Now - startTime).TotalSeconds) > Config.OrderConfig.UserTimeAllowed)
                {
                    var error = "Le visiteur n'est pas parti.";
                    LogUtil.LogError($"{error}. Suppression de la file d'attente, passage à la commande suivante.", Config.IP);
                    order.OrderCancelled(this, $"{error} Votre demande a été supprimée.", false);
                    return OrderResult.NoLeave;
                }

                if (!await IsNetworkSessionActive(token).ConfigureAwait(false))
                {
                    var error = "Crash réseau détecté.";
                    LogUtil.LogError($"{error}. Suppression de la file d'attente, passage à la commande suivante.", Config.IP);
                    order.OrderCancelled(this, $"{error} Votre demande a été supprimée.", true);
                    return OrderResult.Faulted;
                }
            }

            LogUtil.LogInfo($"Commande terminée. Notifier au visiteur que la commande est terminée.", Config.IP);
            await UpdateBlocker(true, token).ConfigureAwait(false);
            order.OrderFinished(this, Config.OrderConfig.CompleteOrderMessage);
            if (order.VillagerName != string.Empty && Config.OrderConfig.EchoArrivingLeavingChannels.Count > 0)
                await AttemptEchoHook($"> Le visiteur a terminé sa commande, et est en train de partir: {order.VillagerName}", Config.OrderConfig.EchoArrivingLeavingChannels, token).ConfigureAwait(false);
            
            await Task.Delay(5_000, token).ConfigureAwait(false);
            await UpdateBlocker(false, token).ConfigureAwait(false);
            await Task.Delay(15_000, token).ConfigureAwait(false);

            // S'assurer que nous sommes sur overworld avant de sortir.
            while (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false) != OverworldState.Overworld)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                await Click(SwitchButton.B, 0_300, token).ConfigureAwait(false);
            }

            // terminer l'animation "circle in
            await Task.Delay(1_200, token).ConfigureAwait(false);
            return OrderResult.Success;
        }

        private async Task RestartGame(CancellationToken token)
        {
            // Match serré
            await Click(SwitchButton.B, 0_500, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await Click(SwitchButton.HOME, 0_800, token).ConfigureAwait(false);
            await Task.Delay(0_300, token).ConfigureAwait(false);

            await Click(SwitchButton.X, 0_500, token).ConfigureAwait(false);
            await Click(SwitchButton.A, 0_500, token).ConfigureAwait(false);

            // Attendez la roue "fermeture du logiciel".
            await Task.Delay(3_500 + Config.RestartGameWait, token).ConfigureAwait(false);

            await Click(SwitchButton.A, 1_000 + Config.RestartGameWait, token).ConfigureAwait(false);

            // Cliquez pour éviter toute mise à jour du système si elle est demandée
            if (Config.AvoidSystemUpdate)
                await Click(SwitchButton.DUP, 0_600, token).ConfigureAwait(false);

            // Début du jeu
            for (int i = 0; i < 2; ++i)
                await Click(SwitchButton.A, 1_000 + Config.RestartGameWait, token).ConfigureAwait(false);

            // Attendez la roue "vérifier si le jeu peut être joué".
            await Task.Delay(5_000 + Config.RestartGameWait, token).ConfigureAwait(false);

            for (int i = 0; i < 3; ++i)
                await Click(SwitchButton.A, 1_000, token).ConfigureAwait(false);
        }

        private async Task EndSession(CancellationToken token)
        {
            for (int i = 0; i < 5; ++i)
                await Click(SwitchButton.B, 0_300, token).ConfigureAwait(false);

            await Task.Delay(0_500, token).ConfigureAwait(false);
            await Click(SwitchButton.MINUS, 0_500, token).ConfigureAwait(false);

            // Fin de la session ou fermeture de la porte ou fermeture du jeu
            for (int i = 0; i < 5; ++i)
                await Click(SwitchButton.A, 1_000, token).ConfigureAwait(false);

            await Task.Delay(14_000, token).ConfigureAwait(false);
        }

        private async Task EnterAirport(CancellationToken token)
        {
            // Mettez en pause les congélateurs pour tenir compte du décalage de l'écran de chargement.
            await SwitchConnection.SetFreezePauseState(true, token).ConfigureAwait(false);
            await Task.Delay(0_200 + Config.ExtraTimeEnterAirportWait, token).ConfigureAwait(false);

            int tries = 0;
            var state = await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false);
            var baseState = state;
            while (baseState == state)
            {
                // Aller à l'aéroport
                LogUtil.LogInfo($"Tentative d'entrée dans l'aéroport. Essayez : {tries + 1}", Config.IP);
                await SetStick(SwitchStick.LEFT, 20_000, 20_000, 0_400, token).ConfigureAwait(false);
                await Task.Delay(0_500, token).ConfigureAwait(false);
                await SetStick(SwitchStick.LEFT, 0, 0, 1_500, token).ConfigureAwait(false);
                await Task.Delay(1_000 + Config.ExtraTimeEnterAirportWait, token).ConfigureAwait(false);

                state = await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false);

                await SetStick(SwitchStick.LEFT, 0, 0, 0_600, token).ConfigureAwait(false);
                await Task.Delay(1_000, token).ConfigureAwait(false);

                tries++;
                if (tries > 6)
                    break;
            }

            tries = 0;
            while (state != OverworldState.Overworld)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                state = await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false);
                tries++;
                if (tries > 5)
                    break;
            }

            // Délai pour l'animation
            await Task.Delay(1_500, token).ConfigureAwait(false);
            await SwitchConnection.SetFreezePauseState(false, token).ConfigureAwait(false);
        }

        private async Task InjectOrder(MapTerrainLite updatedMap, CancellationToken token)
        {
            // Injecter l'ordre sur la carte
            var mapChunks = updatedMap.GenerateReturnBytes(Config.MapPullChunkSize, (uint)OffsetHelper.FieldItemStart);
            for (int i = 0; i < mapChunks.Length; ++i)
                await Connection.WriteBytesAsync(mapChunks[i].ToSend, mapChunks[i].Offset, token).ConfigureAwait(false);
        }

        /// <returns>Si la connexion est active ou non à la fin de la fonction de fermeture de la porte.</returns>
        private async Task<bool> CloseGate(CancellationToken token)
        {
            // Se téléporter à l'ancre d'entrée de l'aéroport (deux fois, au cas où nous serions repoussés).
            await SendAnchorBytes(2, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SendAnchorBytes(2, token).ConfigureAwait(false);

            // Entrer l'aéroport
            await EnterAirport(token).ConfigureAwait(false);

            // Téléportation à Orville (deux fois, au cas où nous serions tirés en arrière)
            await SendAnchorBytes(3, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SendAnchorBytes(3, token).ConfigureAwait(false);

            // Fermer le portail (sécurité intégrée sans vérifier s'il est ouvert)
            await DodoPosition.CloseGate((uint)OffsetHelper.DodoAddress, token).ConfigureAwait(false);

            await Task.Delay(2_000, token).ConfigureAwait(false);

            return await IsNetworkSessionActive(token).ConfigureAwait(false);
        }

        private async Task<bool> EnsureAnchorMatches(int anchorIndex, int millisecondsTimeout, Func<Task> toDoPerLoop, CancellationToken token)
        {
            bool success = false;
            var startTime = DateTime.Now;
            while (!success)
            {
                if (toDoPerLoop != null)
                    await toDoPerLoop().ConfigureAwait(false);

                bool anchorMatches = await DoesAnchorMatch(anchorIndex, token).ConfigureAwait(false);
                if (!anchorMatches)
                    await Task.Delay(0_500, token).ConfigureAwait(false);
                else
                    success = true;

                if (Math.Abs((DateTime.Now - startTime).TotalMilliseconds) > millisecondsTimeout)
                    return false;
            }

            return true;
        }

        // L'ancrage actuel de la RAM correspond-il à celui que nous avons sauvegardé ?
        private async Task<bool> DoesAnchorMatch(int anchorIndex, CancellationToken token)
        {
            var anchorMemory = await ReadAnchor(token).ConfigureAwait(false);
            return anchorMemory.AnchorBytes.SequenceEqual(Anchors.Anchors[anchorIndex].AnchorBytes);
        }

        private async Task EnsureAnchorsAreInitialised(CancellationToken token)
        {
            while (Config.ForceUpdateAnchors || Anchors.IsOneEmpty(out _))
                await Task.Delay(1_000, token).ConfigureAwait(false);
        }

        public async Task<bool> UpdateAnchor(int index, CancellationToken token)
        {
            var anchors = Anchors.Anchors;
            if (index < 0 || index > anchors.Length)
                return false;

            var anchor = await ReadAnchor(token).ConfigureAwait(false);
            var bytesA = anchor.Anchor1;
            var bytesB = anchor.Anchor2;

            anchors[index].Anchor1 = bytesA;
            anchors[index].Anchor2 = bytesB;
            Anchors.Save();
            LogUtil.LogInfo($"Ancrage actualisé {index}.", Config.IP);
            return true;
        }

        public async Task<bool> SendAnchorBytes(int index, CancellationToken token)
        {
            var anchors = Anchors.Anchors;
            if (index < 0 || index > anchors.Length)
                return false;

            ulong offset = await DodoPosition.FollowMainPointer(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false);
            await SwitchConnection.WriteBytesAbsoluteAsync(anchors[index].Anchor1, offset, token).ConfigureAwait(false);
            await SwitchConnection.WriteBytesAbsoluteAsync(anchors[index].Anchor2, offset + 0x3C, token).ConfigureAwait(false);

            return true;
        }

        private async Task<PosRotAnchor> ReadAnchor(CancellationToken token)
        {
            ulong offset = await DodoPosition.FollowMainPointer(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false);
            var bytesA = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 0xC, token).ConfigureAwait(false);
            var bytesB = await SwitchConnection.ReadBytesAbsoluteAsync(offset + 0x3C, 0x4, token).ConfigureAwait(false);
            var sequentinalAnchor = bytesA.Concat(bytesB).ToArray();
            return new PosRotAnchor(sequentinalAnchor);
        }

        private async Task<bool> IsArriverNew(CancellationToken token)
        {
            var data = await Connection.ReadBytesAsync((uint)OffsetHelper.ArriverNameLocAddress, 0x14, token).ConfigureAwait(false);
            var arriverName = Encoding.Unicode.GetString(data).TrimEnd('\0'); // supprimer uniquement les valeurs nulles à la fin
            if (arriverName != string.Empty && arriverName != LastArrival)
            {
                LastArrival = arriverName;
                data = await Connection.ReadBytesAsync((uint)OffsetHelper.ArriverVillageLocAddress, 0x14, token).ConfigureAwait(false);
                LastArrivalIsland = Encoding.Unicode.GetString(data).TrimEnd('\0').TrimEnd();

                LogUtil.LogInfo($"{arriverName} de {LastArrivalIsland} arrive !", Config.IP);

                if (Config.HideArrivalNames)
                {
                    var blank = new byte[0x14];
                    await Connection.WriteBytesAsync(blank, (uint)OffsetHelper.ArriverNameLocAddress, token).ConfigureAwait(false);
                    await Connection.WriteBytesAsync(blank, (uint)OffsetHelper.ArriverVillageLocAddress, token).ConfigureAwait(false);
                }

                return true;
            }
            return false;
        }

        private async Task SaveVillagersToFile(CancellationToken token)
        {
            string DodoDetails = Config.DodoModeConfig.MinimizeDetails ? Villagers.LastVillagers : $"Les villageois sur {TownName}: {Villagers.LastVillagers}";
            byte[] encodedText = Encoding.ASCII.GetBytes(DodoDetails);
            await FileUtil.WriteBytesToFileAsync(encodedText, Config.DodoModeConfig.VillagerFilename, token).ConfigureAwait(false);
        }

        private async Task SaveDodoCodeToFile(CancellationToken token)
        {
            string DodoDetails = Config.DodoModeConfig.MinimizeDetails ? DodoCode : $"{TownName}: {DodoCode}";
            byte[] encodedText = Encoding.ASCII.GetBytes(DodoDetails);
            await FileUtil.WriteBytesToFileAsync(encodedText, Config.DodoModeConfig.DodoRestoreFilename, token).ConfigureAwait(false);
        }

        private async Task SaveLayerNameToFile(string name, CancellationToken token)
        {
            byte[] encodedText = Encoding.ASCII.GetBytes(name);
            await FileUtil.WriteBytesToFileAsync(encodedText, Config.DodoModeConfig.LoadedNHLFilename, token).ConfigureAwait(false);
        }

        private async Task SaveVisitorsToFile(CancellationToken token)
        {
            string VisitorInfo;
            if (VisitorList.VisitorCount == VisitorListHelper.VisitorListSize)
                VisitorInfo = Config.DodoModeConfig.MinimizeDetails ? $"FULL" : $"{TownName} est plein";
            else
            {
                // VisitorList.VisitorCount - 1 car l'hôte est toujours sur l'île.
                uint VisitorCount = VisitorList.VisitorCount - 1;
                VisitorInfo = Config.DodoModeConfig.MinimizeDetails ? $"{VisitorCount}" : $"Visiteurs: {VisitorCount}";
            }

            // nombre de visiteurs
            byte[] encodedText = Encoding.ASCII.GetBytes(VisitorInfo);
            await FileUtil.WriteBytesToFileAsync(encodedText, Config.DodoModeConfig.VisitorFilename, token).ConfigureAwait(false);

            // liste des noms des visiteurs
            encodedText = Encoding.ASCII.GetBytes(VisitorList.VisitorFormattedString);
            await FileUtil.WriteBytesToFileAsync(encodedText, Config.DodoModeConfig.VisitorListFilename, token).ConfigureAwait(false);
        }

        private async Task ResetFiles(CancellationToken token)
        {
            string DodoDetails = Config.DodoModeConfig.MinimizeDetails ? "FETCHING" : $"{TownName}: FETCHING";
            byte[] encodedText = Encoding.ASCII.GetBytes(DodoDetails);
            await FileUtil.WriteBytesToFileAsync(encodedText, Config.DodoModeConfig.DodoRestoreFilename, token).ConfigureAwait(false);

            encodedText = Encoding.ASCII.GetBytes(Config.DodoModeConfig.MinimizeDetails ? "0" : "Visitors: 0");
            await FileUtil.WriteBytesToFileAsync(encodedText, Config.DodoModeConfig.VisitorFilename, token).ConfigureAwait(false);

            encodedText = Encoding.ASCII.GetBytes(Config.DodoModeConfig.MinimizeDetails ? "No-one" : "No visitors");
            await FileUtil.WriteBytesToFileAsync(encodedText, Config.DodoModeConfig.VisitorListFilename, token).ConfigureAwait(false);
        }

        private async Task<bool> IsNetworkSessionActive(CancellationToken token) => (await Connection.ReadBytesAsync((uint)OffsetHelper.OnlineSessionAddress, 0x1, token).ConfigureAwait(false))[0] == 1;

        private async Task DropLoop(CancellationToken token)
        {
            if (!Config.AcceptingCommands)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                return;
            }

            // les discours sont prioritaires
            if (Speaks.TryDequeue(out var chat))
            {
                LogUtil.LogInfo($"Maintenant, je parle: {chat.User}:{chat.Item}", Config.IP);
                await Speak(chat.Item, token).ConfigureAwait(false);
            }

            if (StonkRequests.TryDequeue(out var stonk))
            {
                await UpdateTurnips(stonk.Item, token).ConfigureAwait(false);
                stonk.OnFinish?.Invoke(true);
            }

            if (Injections.TryDequeue(out var item))
            {
                var count = await DropItems(item, token).ConfigureAwait(false);
                State.AfterDrop(count);
            }
            else if ((State.CleanRequired && State.Config.AutoClean) || CleanRequested)
            {
                await CleanUp(State.Config.PickupCount, token).ConfigureAwait(false);
                State.AfterClean();
                CleanRequested = false;
            }
            else
            {
                State.StillIdle();
                await Task.Delay(0_300, token).ConfigureAwait(false);
            }
        }

        private async Task Speak(string toSpeak, CancellationToken token)
        {
            // obtenir l'adresse du chat
            ChatAddress = await DodoPosition.FollowMainPointer(OffsetHelper.ChatCoordJumps, token).ConfigureAwait(false);
            await Task.Delay(0_200, token).ConfigureAwait(false);

            await Click(SwitchButton.R, 0_500, token).ConfigureAwait(false);
            await Click(SwitchButton.A, 0_400, token).ConfigureAwait(false);
            await Click(SwitchButton.A, 0_400, token).ConfigureAwait(false);

            // Injecter le texte en utf-16, et null le reste
            var chatBytes = Encoding.Unicode.GetBytes(toSpeak);
            var sendBytes = new byte[OffsetHelper.ChatBufferSize * 2];
            Array.Copy(chatBytes, sendBytes, chatBytes.Length);
            await SwitchConnection.WriteBytesAbsoluteAsync(sendBytes, ChatAddress, token).ConfigureAwait(false);

            await Click(SwitchButton.PLUS, 0_200, token).ConfigureAwait(false);

            // Sortir de tous les menus (sécurité intégrée)
            for (int i = 0; i < 2; i++)
                await Click(SwitchButton.B, 0_400, token).ConfigureAwait(false);
        }

        private async Task UpdateTurnips(int newStonk, CancellationToken token)
        {
            var stonkBytes = await Connection.ReadBytesAsync((uint)OffsetHelper.TurnipAddress, TurnipStonk.SIZE, token).ConfigureAwait(false); 
            var newStonkBytes = BitConverter.GetBytes(newStonk);
            for (int i = 0; i < 12; ++i)
                Array.Copy(newStonkBytes, 0, stonkBytes, 12 + (i * 4), newStonkBytes.Length);
            await Connection.WriteBytesAsync(stonkBytes, (uint)OffsetHelper.TurnipAddress, token).ConfigureAwait(false); 
        }

        private async Task<bool> GetIsPlayerInventoryValid(uint playerOfs, CancellationToken token)
        {
            var (ofs, len) = InventoryValidator.GetOffsetLength(playerOfs);
            var inventory = await Connection.ReadBytesAsync(ofs, len, token).ConfigureAwait(false);

            return InventoryValidator.ValidateItemBinary(inventory);
        }

        private async Task<int> DropItems(ItemRequest drop, CancellationToken token)
        {
            int dropped = 0;
            bool first = true;
            foreach (var item in drop.Item)
            {
                await DropItem(item, first, token).ConfigureAwait(false);
                first = false;
                dropped++;
            }
            return dropped;
        }

        private async Task DropItem(Item item, bool first, CancellationToken token)
        {
            // Sortez de tous les menus.
            if (first)
            {
                for (int i = 0; i < 3; i++)
                    await Click(SwitchButton.B, 0_400, token).ConfigureAwait(false);
            }

            var itemName = GameInfo.Strings.GetItemName(item);
            LogUtil.LogInfo($"Injection de l'article: {item.DisplayItemId:X4} ({itemName}).", Config.IP);
            Item[]? startItems = null;

            // Injection d'un article dans l'ensemble de l'inventaire
            if (!Config.DropConfig.UseLegacyDrop)
            {
                // Stock de départ
                InjectionResult result;
                (result, startItems) = await PocketInjector.Read(token).ConfigureAwait(false);
                if (result != InjectionResult.Success)
                    LogUtil.LogInfo($"L'échec de la lecture : {result}", Config.IP);

                // Injecter notre élément sûr à déposer
                await PocketInjector.Write40(PocketInjector.DroppableOnlyItem, token);
                await Task.Delay(0_300, token).ConfigureAwait(false);

                // Ouvrez l'inventaire du joueur et cliquez sur A pour arriver à survoler la sélection "déposer un objet".
                await Click(SwitchButton.X, 1_200, token).ConfigureAwait(false);
                await Click(SwitchButton.A, 0_500, token).ConfigureAwait(false);

                // Injecter le bon élément
                await PocketInjector.Write40(item, token);
                await Task.Delay(0_300, token).ConfigureAwait(false);
            }
            else
            {
                var data = item.ToBytesClass();
                var poke = SwitchCommand.Poke(InventoryOffset, data);
                await Connection.SendAsync(poke, token).ConfigureAwait(false);
                await Task.Delay(0_300, token).ConfigureAwait(false);

                // Ouvre l'inventaire du joueur et ouvre l'emplacement de l'objet actuellement sélectionné -- supposé être le décalage de la configuration.
                await Click(SwitchButton.X, 1_100, token).ConfigureAwait(false);
                await Click(SwitchButton.A, 0_500, token).ConfigureAwait(false);

                // Naviguez jusqu'à l'option "déposer un élément".
                var downCount = item.GetItemDropOption();
                for (int i = 0; i < downCount; i++)
                    await Click(SwitchButton.DDOWN, 0_400, token).ConfigureAwait(false);
            }

            // Déposer un élément, fermer le menu.
            await Click(SwitchButton.A, 0_400, token).ConfigureAwait(false);
            await Click(SwitchButton.X, 0_400, token).ConfigureAwait(false);

            // Sortir de tous les menus (sécurité intégrée)
            for (int i = 0; i < 2; i++)
                await Click(SwitchButton.B, 0_400, token).ConfigureAwait(false);

            // rétablir l'inventaire de départ si nécessaire
            if (startItems != null)
                await PocketInjector.Write(startItems, token).ConfigureAwait(false);
        }

        private async Task CleanUp(int count, CancellationToken token)
        {
            LogUtil.LogInfo("Ramasser les restes d'articles pendant les temps morts.", Config.IP);

            // Sortez de tous les menus.
            for (int i = 0; i < 3; i++)
                await Click(SwitchButton.B, 0_400, token).ConfigureAwait(false);

            var poke = SwitchCommand.Poke(InventoryOffset, Item.NONE.ToBytes());
            await Connection.SendAsync(poke, token).ConfigureAwait(false);

            // Ramassez et effacez.
            for (int i = 0; i < count; i++)
            {
                await Click(SwitchButton.Y, 2_000, token).ConfigureAwait(false);
                await Connection.SendAsync(poke, token).ConfigureAwait(false);
                await Task.Delay(1_000, token).ConfigureAwait(false);
            }
        }

        // Supplémentaire
        private readonly byte[] MaxTextSpeed = new byte[1] { 3 };
        public async Task ClickConversation(SwitchButton b, int delay, CancellationToken token)
        {
            await Connection.WriteBytesAsync(MaxTextSpeed, (int)OffsetHelper.TextSpeedAddress, token).ConfigureAwait(false);
            await Click(b, delay, token).ConfigureAwait(false);
        }

        public async Task SetScreenCheck(bool on, CancellationToken token, bool force = false)
        {
            if (!Config.ExperimentalSleepScreenOnIdle && !force)
                return;
            await SetScreen(on, token).ConfigureAwait(false);
        }

        public async Task UpdateBlocker(bool show, CancellationToken token) => await FileUtil.WriteBytesToFileAsync(show ? Encoding.UTF8.GetBytes(Config.BlockerEmoji) : Array.Empty<byte>(), "blocker.txt", token).ConfigureAwait(false);

        private void NotifyDodo(string dodo)
        {
            foreach (var n in DodoNotifiers)
                n.NotifyServerOfDodoCode(dodo);
        }

        private void NotifyState(GameState st)
        {
            foreach (var n in DodoNotifiers)
                n.NotifyServerOfState(st);
        }
        
    }
}
