using System.Collections.Concurrent;
using System.Numerics;
using Eco.Core.Controller;
using Eco.Core.Utils;
using Eco.Gameplay.Aliases;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Economy;
using Eco.Gameplay.Economy.Transfer;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.UI;
using Eco.Minecarts.Physics;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Shared.Utils;

namespace Eco.Minecarts.Runtime;

[Serialized]
public sealed class TrainFareData
{
    [Serialized] public string ServiceId { get; set; } = Guid.NewGuid().ToString("N");
    [Serialized] public string ServiceName { get; set; } = "Passenger service";
    [Serialized] public string OwnerSignature { get; set; } = "";
    [Serialized] public bool Enabled { get; set; }
    [Serialized] public bool FreePublic { get; set; }
    [Serialized] public float Price { get; set; } = 10;
    [Serialized] public float Minutes { get; set; } = 120;
    [Serialized] public Currency? Currency { get; set; }
    [Serialized] public string ItemName { get; set; } = "";
    [Serialized] public int RevenueUserId { get; set; }
    [Serialized] public int Revision { get; set; }
    [Serialized, ThreadSafe] public List<int> Cars { get; set; } = new();
    [Serialized, ThreadSafe] public List<Guid> CarObjects { get; set; } = new();
    [Serialized, ThreadSafe] public List<int> Managers { get; set; } = new();
    [Serialized, ThreadSafe] public List<int> FreeUsers { get; set; } = new();
    [Serialized, ThreadSafe] public List<PassengerTicket> Tickets { get; set; } = new();
    public TrainFareData Copy()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/Copy");
        var snapshot = CopySettings();
        snapshot.Tickets = Tickets.Select(t => t.Copy()).ToList();
        return snapshot;
    }
    // Fare views and boarding quotes need configuration, not the potentially
    // 10,000-entry ticket ledger. Persistence still uses the complete Copy().
    internal TrainFareData CopySettings()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/Copy settings");
        return new()
        {
            ServiceId=ServiceId,ServiceName=ServiceName,OwnerSignature=OwnerSignature,Enabled=Enabled,FreePublic=FreePublic,
            Price=Price,Minutes=Minutes,Currency=Currency,ItemName=ItemName,RevenueUserId=RevenueUserId,Revision=Revision,
            Cars=Cars.ToList(),CarObjects=CarObjects.ToList(),Managers=Managers.ToList(),FreeUsers=FreeUsers.ToList()
        };
    }
}

[Serialized, NoIcon, AutogenClass, LocDisplayName("Passenger Service Fares")]
public sealed class TrainFareComponent : WorldObjectComponent, IPersistentData
{
    private static readonly ConcurrentDictionary<int,byte> Pending = new();
    private readonly object gate = new();
    [Serialized] private TrainFareData data = new();
    private long fareTopologyRevision=-1;
    private RailCouplingComponent[] fareMembers=[];
    private TrainFareComponent? fareService;
    private string? fareServiceId;
    private RailVehicleObject Vehicle => (RailVehicleObject)Parent;
    // Unlike the physics leader, this anchor never changes when someone mounts.
    internal TrainFareComponent Service => Parent==null ? this : Parent.GetComponent<RailCouplingComponent>().Group()
        .Where(c=>c.Parent.GetComponent<TrainFareComponent>()!=null)
        .OrderByDescending(c=>c.Vehicle.RailSpec.Powered).ThenByDescending(c=>c.Vehicle.DriverPriority).ThenBy(c=>c.Parent.ObjectID)
        .First().Parent.GetComponent<TrainFareComponent>();
    private TrainFareData Snapshot { get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/TrainFareComponent.Snapshot.get", this.Parent); var service=Service; lock(service.gate) { service.CheckOwner(); return service.data.CopySettings(); } } }
    public object PersistentData
    {
        get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/TrainFareComponent.PersistentData.get", this.Parent); lock(gate) return data.Copy(); }
        set { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/TrainFareComponent.PersistentData.set", this.Parent); if(value is TrainFareData saved) lock(gate) data=saved.Copy(); }
    }
    [SyncToView, Autogen] public string ServiceName => Snapshot.ServiceName;
    public string ServiceId => Snapshot.ServiceId;
    [SyncToView, Autogen] public bool PaidAccessEnabled => Snapshot.Enabled;
    [SyncToView, Autogen] public float FarePrice => Snapshot.Price;
    [SyncToView, Autogen, LocDescription("Enter currency:<currency name> or item:<item type name>.")]
    public string Payment
    {
        get
        {
            // Use one locked snapshot when the payment type changes during a read.
            var snapshot = Snapshot;
            return snapshot.ItemName.Length>0 ? "item:"+snapshot.ItemName
                : snapshot.Currency is {} currency ? "currency:"+currency.Name : "currency:";
        }
    }
    [SyncToView, Autogen, LocDisplayName("Ticket Validity (minutes)")] public float AccessMinutes => Snapshot.Minutes;
    // Retain the old view/RPC contract, but use a named chooser in new panels.
    [SyncToView]
    public string EligibleCarIds
    {
        get
        {
            var snapshot=Snapshot;
            if(snapshot.Cars.Count>0 && snapshot.CarObjects.Count==0) return "Reselect cars";
            if(snapshot.CarObjects.Count==0) return "all";
            var choices=PassengerCars(Service);
            return string.Join(",",choices.Select((c,i)=>(Car:c,Number:i+1)).Where(c=>snapshot.CarObjects.Contains(c.Car.Parent.ObjectID)).Select(c=>c.Number));
        }
    }
    [SyncToView] public string ConnectedPassengerCars => string.Join(", ",PassengerCars(Service).Select((c,i)=>$"{i+1}: {c.Parent.DisplayName}"));
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Eligible Passenger Cars")]
    public string EligiblePassengerCars
    {
        get
        {
            var service=Service;TrainFareData snapshot;
            lock(service.gate){service.CheckOwner();snapshot=service.data.CopySettings();}
            if(snapshot.CarObjects.Count==0)return snapshot.Cars.Count==0?"All passenger cars":"Reselect passenger cars after update";
            var available=PassengerCars(service).Select(c=>c.Parent.ObjectID).ToHashSet();
            var selected=snapshot.CarObjects.Distinct().ToArray();var missing=selected.Count(id=>!available.Contains(id));
            return $"{selected.Length} selected passenger cars"+(missing>0?$" ({missing} unavailable; choose cars to review)":"");
        }
    }
    [RPC, Autogen, UITypeName("BigButton")]
    public void ChoosePassengerCars(Player player)=>_=ChoosePassengerCarsAsync(player);
    private async Task ChoosePassengerCarsAsync(Player player)
    {
        if(!CanManage(player)){if(player!=null)Denied(player);return;}
        try
        {
            var service=Service;TrainFareData original;
            lock(service.gate){if(!CanManage(player)||Service!=service)return;original=service.data.CopySettings();}
            var members=service.Parent.GetComponent<RailCouplingComponent>().Group();
            var cars=members.Where(c=>c.Vehicle.RailSpec.PassengerSeats>0).ToArray();
            var originalIds=original.CarObjects.ToHashSet();
            var selected=cars.Where(c=>originalIds.Contains(c.Parent.ObjectID)).Select(c=>c.Parent.ObjectID).ToHashSet();
            var unavailable=originalIds.Except(selected).Any() || original.Cars.Count>0&&originalIds.Count==0;
            bool CurrentService()
            {
                if(!CanManage(player)||Service!=service || original.ServiceId!=service.data.ServiceId || original.Revision!=service.data.Revision)return false;
                var current=service.Parent.GetComponent<RailCouplingComponent>().Group();
                // Other trains can couple while this menu is open. Only this
                // service's membership and authority govern the saved scope.
                return current.Length==members.Length && current.ToHashSet().SetEquals(members)
                    && members.All(c=>!c.Parent.IsDestroyed);
            }
            var page=0;
            while(true)
            {
                var entries=new List<(string Label,int Car,int Step)>{("Use all passenger cars"+(originalIds.Count==0&&original.Cars.Count==0?" (current)":""),-1,0)};
                for(var i=page*32;i<Math.Min(cars.Length,(page+1)*32);i++)
                {
                    var car=cars[i];
                    entries.Add(($"[{(selected.Contains(car.Parent.ObjectID)?"x":" ")}] {i+1}. {car.Parent.DisplayName}",i,0));
                }
                if(page>0)entries.Add(("< Previous page",-1,-1));
                if((page+1)*32<cars.Length)entries.Add(("Next page >",-1,1));
                if(selected.Count>0)entries.Add(($"Apply {selected.Count} selected cars",-2,0));
                var title=cars.Length==0?"No passenger cars on this service. Use all passenger cars for automatic selection.":
                    "Toggle passenger cars, then Apply. Use all passenger cars for automatic selection. Cancel keeps the current setting.";
                if(unavailable)title+=" Some saved cars are unavailable; Apply replaces the selection.";
                var pick=await player.OptionBox(Localizer.DoStr(title),entries.Select(e=>e.Label).ToList());
                if(pick<0||pick>=entries.Count)return;
                lock(service.gate)
                {
                    if(!CurrentService())
                    {player.InfoBoxLoc($"Passenger service or connected cars changed; reopen the passenger car chooser.");return;}
                    var entry=entries[pick];
                    if(entry.Step!=0){page+=entry.Step;continue;}
                    if(entry.Car>=0)
                    {
                        var id=cars[entry.Car].Parent.ObjectID;
                        if(!selected.Remove(id))selected.Add(id);
                        continue;
                    }
                    var ids=entry.Car==-1?new HashSet<Guid>():selected;
                    if(service.data.Cars.Count==0&&originalIds.SetEquals(ids))return;
                    service.data.Cars.Clear();
                    service.data.CarObjects=cars.Where(c=>ids.Contains(c.Parent.ObjectID)).Select(c=>c.Parent.ObjectID).ToList();
                    service.Publish();return;
                }
            }
        }
        catch(TimeoutException) { }
        catch(Exception error){Eco.Shared.Logging.Log.WriteErrorLineLoc($"Passenger car selection failed: {error}");}
    }
    [SyncToView, Autogen] public string FareRecipient => Service.ServiceOwners.FirstOrDefault(u=>u.Id==Snapshot.RevenueUserId)?.Name ?? "";
    [SyncToView, Autogen, LocDescription("Exact usernames separated by commas; enter none to clear.")] public string FareManagers => UserNames(Snapshot.Managers);
    [SyncToView, Autogen, LocDescription("Exact usernames separated by commas; enter none to clear.")] public string FreePassengers => UserNames(Snapshot.FreeUsers);
    [SyncToView, Autogen, LocDisplayName("Free Travel When Fares Are Off")] public bool FreePublicWhenDisabled => Snapshot.FreePublic;
    private static RailCouplingComponent[] PassengerCars(TrainFareComponent service) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/PassengerCars"); return service.Parent.GetComponent<RailCouplingComponent>().Group().Where(c=>c.Vehicle.RailSpec.PassengerSeats>0).ToArray(); }
    private static string UserNames(List<int> ids) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/UserNames"); return string.Join(", ",UserManager.Users.Where(u=>ids.Contains(u.Id)).Select(u=>u.Name)); }
    private User[] ServiceOwners => Parent.Owners == null ? Array.Empty<User>() : new[] { Parent.Owners }.ToUsers().ToArray();
    private void CheckOwner()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/CheckOwner", this.Parent);
        var owners=ServiceOwners;
        var signature=string.Join(",",owners.Select(u=>u.Id).Order());
        if(data.OwnerSignature==signature) return;
        if(data.OwnerSignature.Length>0)
        {
            data.Enabled=false; data.FreePublic=false; data.Managers.Clear(); data.FreeUsers.Clear(); data.Tickets.Clear();
            data.ServiceId=Guid.NewGuid().ToString("N"); data.Revision++;
        }
        data.OwnerSignature=signature;
        data.RevenueUserId=owners.FirstOrDefault()?.Id ?? 0;
        Parent.SetDirty();
    }
    private bool CanManage(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/CanManage", this.Parent);
        if(player==null || Parent==null || Parent.IsDestroyed || !RailWorldDistance.Within(player.User.Position,Parent.Position,Vehicle.CouplerOffset+4)) return false;
        var service=Service;
        lock(service.gate) { service.CheckOwner(); return service.ServiceOwners.Contains(player.User) || service.data.Managers.Contains(player.User.Id); }
    }
    private void Publish()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/Publish", this.Parent);
        data.Revision++; Parent.SetDirty();
        foreach(var c in Parent.GetComponent<RailCouplingComponent>().Group()) c.Parent.GetComponent<TrainFareComponent>()?.NotifyFields();
    }
    private void NotifyFields()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/NotifyFields", this.Parent);
        foreach(var name in new[]{nameof(ServiceName),nameof(ServiceId),nameof(PaidAccessEnabled),nameof(FarePrice),nameof(Payment),nameof(AccessMinutes),nameof(EligibleCarIds),nameof(ConnectedPassengerCars),nameof(EligiblePassengerCars),nameof(FareRecipient),nameof(FareManagers),nameof(FreePassengers),nameof(FreePublicWhenDisabled)}) this.Changed(name);
    }
    private void Denied(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/Denied", this.Parent); player.InfoBoxLoc($"Only the service owner and designated fare managers can change passenger fares."); }
    private void Edit(Player player,Action<TrainFareComponent> change)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/Edit", this.Parent);
        if(!CanManage(player)) { if(player!=null) Denied(player); return; }
        var service=Service;
        lock(service.gate)
        {
            if(!CanManage(player) || Service!=service) return;
            change(service);
        }
    }
    [RPC] public void SetServiceName(Player player,string value) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/SetServiceName", this.Parent); Edit(player,service=>
    {
        var name=value?.Trim();
        if(string.IsNullOrEmpty(name)) { player.InfoBoxLoc($"Enter a service name."); return; }
        name=name[..Math.Min(name.Length,80)];
        if(service.data.ServiceName==name) return;
        service.data.ServiceName=name; service.Publish();
    }); }
    [RPC] public void SetFarePrice(Player player,float value) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/SetFarePrice", this.Parent); Edit(player,service=>
    {
        if(!PassengerFareRules.ValidPrice(value,service.data.ItemName.Length>0)) { player.InfoBoxLoc($"Enter a valid fare price."); return; }
        var price=MathF.Round(value,2);
        if(service.data.Price==price) return;
        service.data.Price=price; service.Publish();
    }); }
    [RPC] public void SetPayment(Player player,string value) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/SetPayment", this.Parent); Edit(player,service=>
    {
        var payment=value?.Trim()??"";
        Currency? currency=null; string itemName="";
        if(payment.StartsWith("currency:",StringComparison.OrdinalIgnoreCase))
            currency=CurrencyManager.Currencies.FirstOrDefault(c=>string.Equals(c.Name,payment[9..].Trim(),StringComparison.OrdinalIgnoreCase));
        else if(payment.StartsWith("item:",StringComparison.OrdinalIgnoreCase)) itemName=payment[5..].Trim();
        var item=itemName.Length>0?Item.Get(itemName):null;
        if((currency==null && item==null) || (item!=null && (Item.TypeIsUnique(item.GetType()) || !PassengerFareRules.ValidPrice(service.data.Price,true))))
        { player.InfoBoxLoc($"Use currency:<exact currency name> or item:<item type name>. Item fares need a whole-number price."); return; }
        if(service.data.Currency==currency && service.data.ItemName==itemName) return;
        service.data.Currency=currency; service.data.ItemName=itemName; service.Publish();
    }); }
    [RPC] public void SetAccessMinutes(Player player,float value) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/SetAccessMinutes", this.Parent); Edit(player,service=>
    {
        if(!float.IsFinite(value) || value<1 || value>525600) { player.InfoBoxLoc($"Ticket validity must be between 1 and 525600 minutes."); return; }
        if(service.data.Minutes==value) return;
        service.data.Minutes=value; service.Publish();
    }); }
    [RPC] public void SetEligibleCarIds(Player player,string value) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/SetEligibleCarIds", this.Parent); Edit(player,service=>
    {
        var text=value?.Trim()??"";
        var choices=PassengerCars(service);
        var ids=new List<Guid>();
        if(!string.Equals(text,"all",StringComparison.OrdinalIgnoreCase))
        {
            if(text.Length==0) { player.InfoBoxLoc($"Enter all or passenger-car numbers from the list."); return; }
            foreach(var token in text.Split(',',StringSplitOptions.TrimEntries))
            {
                if(!int.TryParse(token,out var index) || index<1 || index>choices.Length)
                { player.InfoBoxLoc($"Choose a listed passenger-car number, or enter all."); return; }
                ids.Add(choices[index-1].Parent.ObjectID);
            }
        }
        // Number order and duplicates do not change the persistent car scope.
        // A legacy numeric scope must still be explicitly replaced.
        if(service.data.Cars.Count==0 && ids.ToHashSet().SetEquals(service.data.CarObjects)) return;
        service.data.Cars.Clear(); service.data.CarObjects=ids.Distinct().ToList(); service.Publish();
    }); }
    [RPC] public void SetFareRecipient(Player player,string value) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/SetFareRecipient", this.Parent); Edit(player,service=>
    {
        var recipient=service.ServiceOwners.FirstOrDefault(u=>string.Equals(u.Name,value?.Trim(),StringComparison.OrdinalIgnoreCase));
        if(recipient==null) { player.InfoBoxLoc($"The fare recipient must be a service owner."); return; }
        if(service.data.RevenueUserId==recipient.Id) return;
        service.data.RevenueUserId=recipient.Id; service.Publish();
    }); }
    private void SetUsers(Player player,string value,bool managers) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/SetUsers", this.Parent); Edit(player,service=>
    {
        var text=value?.Trim()??"";
        var ids=new List<int>();
        if(text.Length>0 && !string.Equals(text,"none",StringComparison.OrdinalIgnoreCase))
            foreach(var token in text.Split(',',StringSplitOptions.TrimEntries))
            {
                var user=UserManager.Users.FirstOrDefault(u=>string.Equals(u.Name,token,StringComparison.OrdinalIgnoreCase));
                if(user==null) { player.InfoBoxLoc($"Unknown user: {token}. Nothing changed."); return; }
                ids.Add(user.Id);
            }
        if(ids.ToHashSet().SetEquals(managers?service.data.Managers:service.data.FreeUsers)) return;
        if(managers) service.data.Managers=ids.Distinct().ToList(); else service.data.FreeUsers=ids.Distinct().ToList();
        service.Publish();
    }); }
    [RPC] public void SetFareManagers(Player player,string value) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/SetFareManagers", this.Parent); SetUsers(player,value,true); }
    [RPC] public void SetFreePassengers(Player player,string value) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/SetFreePassengers", this.Parent); SetUsers(player,value,false); }
    [RPC] public void SetPaidAccessEnabled(Player player,bool value) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/SetPaidAccessEnabled", this.Parent); Edit(player,service=>
    {
        if(value && !ValidPayment(service.data)) { player.InfoBoxLoc($"Configure a valid fare and payment first."); return; }
        if(service.data.Enabled==value) return;
        service.data.Enabled=value; service.Publish();
    }); }
    [RPC] public void SetFreePublicWhenDisabled(Player player,bool value) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/SetFreePublicWhenDisabled", this.Parent); Edit(player,service=>
    { if(service.data.FreePublic==value) return; service.data.FreePublic=value; service.Publish(); }); }
    private static bool ValidPayment(TrainFareData d) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/ValidPayment"); return PassengerFareRules.ValidPrice(d.Price,d.ItemName.Length>0)
        && (d.Cars.Count==0 || d.CarObjects.Count>0)
        && float.IsFinite(d.Minutes) && d.Minutes>=1 && d.Minutes<=525600
        && (d.ItemName.Length==0 ? d.Currency!=null : Item.Get(d.ItemName)!=null && !Item.TypeIsUnique(Item.Get(d.ItemName).GetType())); }
    private bool Free(User user,TrainFareData d,RailVehicleObject car) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/Free", this.Parent); return ServiceOwners.Contains(user) || d.Managers.Contains(user.Id) || d.FreeUsers.Contains(user.Id)
        || Parent.IsAuthorized(user,AccessType.FullAccess) || car.IsAuthorized(user,AccessType.FullAccess); }
    internal bool HasAccess(Player player,RailVehicleObject car)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/HasAccess", this.Parent);
        var service=Service; lock(service.gate)
        {
            service.CheckOwner(); var d=service.data;
            if(d.Enabled && d.Cars.Count>0 && d.CarObjects.Count==0) return service.Free(player.User,d,car);
            if(!d.Enabled || (d.CarObjects.Count>0 && !d.CarObjects.Contains(car.ObjectID))) return service.Free(player.User,d,car) || d.FreePublic || car.IsAuthorized(player.User,AccessType.ConsumerAccess);
            return service.Free(player.User,d,car) || PassengerFareRules.HasTicket(d.Tickets,d.ServiceId,player.User.Id,car.ObjectID,DateTime.UtcNow.Ticks);
        }
    }
    private static bool BoardingSeatValid(Player player,RailVehicleObject car,int seat) => player!=null && car!=null && !car.IsDestroyed && player.User.Player==player
        && !player.MountManager.IsMounted && RailWorldDistance.Within(player.User.Position,car.Position,car.CouplerOffset+3)
        && seat>0 && seat<=car.RailSpec.PassengerSeats && seat<car.GetComponent<MountComponent>().Seats && car.GetComponent<MountComponent>().OccupantIDs[seat]<0;
    private static bool BoardingValid(Player player,RailVehicleObject car,int seat) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/BoardingValid"); return BoardingSeatValid(player,car,seat)
        // Eco's native MountAction requires consumer access before it runs the
        // ticket validator. Never charge a rider who cannot reach that step.
        && car.IsAuthorized(player.User,AccessType.ConsumerAccess); }
    internal void RequestBoard(Player player,RailVehicleObject car,int seat)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/RequestBoard", this.Parent);
        if(!BoardingSeatValid(player,car,seat)) return;
        if(!car.IsAuthorized(player.User,AccessType.ConsumerAccess))
        { player.InfoBoxLoc($"Passenger boarding is closed. Ask the service owner to allow consumer access to this car."); return; }
        if(!Pending.TryAdd(player.User.Id,0)) return;
        _=Task.Run(()=>BoardAsync(player,car,seat));
    }
    private async Task BoardAsync(Player player,RailVehicleObject car,int seat)
    {
        try
        {
            var service=Service;
            // Dispatch is asynchronous even for free/authorized riders. Recheck the
            // current player, pose and requested seat before native mounting.
            if(HasAccess(player,car))
            {
                if(BoardingValid(player,car,seat)) car.GetComponent<MountComponent>().TryMountTargetSeat(player,seat);
                return;
            }
            var quote=Snapshot;
            if(!quote.Enabled || !ValidPayment(quote)) { player.InfoBoxLoc($"Passenger access is unavailable. Ask the service owner to check permissions and fare configuration."); return; }
            if(quote.CarObjects.Count>0 && !quote.CarObjects.Contains(car.ObjectID))
            { player.InfoBoxLoc($"This car is not included in the paid passenger service."); return; }
            var scope=quote.CarObjects.Count==0 ? "all passenger cars on this service" : $"{quote.CarObjects.Count} designated passenger cars";
            var payment=quote.ItemName.Length>0 ? quote.ItemName+" items from your inventory" : quote.Currency!.Name+" from your personal wallet";
            if(!await player.ConfirmBoxLoc($"Ride {quote.ServiceName} for {quote.Minutes:0.##} minutes for {quote.Price:0.##} {payment}? Access covers {scope}." ).WaitAsync(TimeSpan.FromMinutes(2))) return;
            lock(service.gate)
            {
                using var paymentProfile = RailProfile.Measure("Passengers/Fares and payment/Confirmed payment", service.Parent);
                if(!BoardingValid(player,car,seat) || Service!=service || car.GetComponent<TrainFareComponent>().Service!=service) return;
                service.CheckOwner();
                if(service.data.ServiceId!=quote.ServiceId || service.data.Revision!=quote.Revision)
                { player.InfoBoxLoc($"Service settings changed. Nothing was charged; try boarding again."); return; }
                if(service.data.CarObjects.Count>0 && !service.data.CarObjects.Contains(car.ObjectID))
                { player.InfoBoxLoc($"This car is not included in the paid passenger service. Nothing was charged."); return; }
                if(service.HasAccess(player,car)) { car.GetComponent<MountComponent>().TryMountTargetSeat(player,seat); return; }
                service.data.Tickets.RemoveAll(t=>t.ExpiresUtcTicks<=DateTime.UtcNow.Ticks);
                if(service.data.Tickets.Count>=10000) { player.InfoBoxLoc($"This service cannot issue another ticket right now. Nothing was charged."); return; }
                var recipient=service.ServiceOwners.FirstOrDefault(u=>u.Id==quote.RevenueUserId);
                if(recipient==null) { player.InfoBoxLoc($"Fare recipient is no longer an owner. Nothing was charged."); return; }
                var ticket=new PassengerTicket { ServiceId=quote.ServiceId,UserId=player.User.Id,CarObjects=quote.CarObjects.ToList(),ExpiresUtcTicks=DateTime.UtcNow.AddMinutes(quote.Minutes).Ticks };
                Result result;
                if(quote.ItemName.Length>0)
                    // Explicitly confirmed, scoped system transfer: paying never grants cargo permissions.
                    result=player.User.Inventory.TryMoveItems(Item.GetType(quote.ItemName),(int)quote.Price,service.Parent.GetComponent<PublicStorageComponent>().Inventory);
                else
                    result=Transfers.TransferNow(player.User,new TransferData { Currency=quote.Currency!,Amount=quote.Price,SourceAccount=player.User.BankAccount,TargetAccount=recipient.BankAccount,Sender=player.User,TransferAsMuchAsPossible=false,TransferDescription=Localizer.DoStr("Passenger fare: "+quote.ServiceName) });
                if(!result.Success) { player.InfoBoxLoc($"Unable to pay the fare (insufficient funds/items, receiving storage restrictions, or a denied transaction): {result.Message}"); return; }
                service.data.Tickets.Add(ticket); service.Parent.SetDirty();
            }
            if(BoardingValid(player,car,seat) && HasAccess(player,car)) car.GetComponent<MountComponent>().TryMountTargetSeat(player,seat);
        }
        catch(TimeoutException) { }
        catch(Exception error) { Eco.Shared.Logging.Log.WriteErrorLineLoc($"Passenger fare interaction failed: {error}"); player.InfoBoxLoc($"Passenger fare interaction failed. Please report this to the server owner."); }
        finally { Pending.TryRemove(player.User.Id,out _); }
    }
    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Fares and payment/Tick", this.Parent);
        base.Tick();
        if(Parent.IsDestroyed) return;
        var refresh=false;
        var revision=RailCouplingComponent.TopologyRevision;
        if(fareTopologyRevision!=revision)
        {
            // Cache the service together with its order. Only committed link
            // changes require anchor selection, including interior reversals.
            fareService=Service;
            var members=fareService.Parent.GetComponent<RailCouplingComponent>().Group();
            refresh=!fareMembers.SequenceEqual(members);
            fareMembers=members;
            fareTopologyRevision=revision;
        }
        var service=fareService!;
        lock(service.gate)
        {
            // Poll native ownership once per service on the existing tick,
            // including powered anchors without passenger seats. Getters can
            // also reset ownership before this tick; ServiceId witnesses both.
            if(service==this)service.CheckOwner();
            if(fareServiceId!=service.data.ServiceId)
            {
                fareServiceId=service.data.ServiceId;
                refresh=true;
            }
        }
        // Never notify views under either the link lock or the fare lock.
        // Followers that tick before their anchor observe its reset next tick.
        if(refresh)NotifyFields();
        if(Vehicle.RailSpec.PassengerSeats==0) return;
        // Revoked/expired tickets block new boarding immediately, but must not
        // throw seated passengers from a moving train or coaster.
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        if(!PassengerFareRules.SafeToEndRide(motion.CurrentRailVelocity.Length())) return;
        var mounts=Parent.GetComponent<MountComponent>();
        foreach(var player in mounts.MountedPlayers.ToArray())
        {
            if(HasAccess(player,Vehicle))continue;
            // Permission checks can wait for the fare gate, and a previous
            // dismount can run native callbacks. Observe speed again before
            // ending this ride rather than reusing the tick's stopped pose.
            if(!PassengerFareRules.SafeToEndRide(motion.CurrentRailVelocity.Length()))return;
            // The seated snapshot can also outlive a voluntary dismount.
            if(player.MountManager.Mount!=mounts)continue;
            mounts.TryDismountPlayer(player);
            player.InfoBoxLoc($"Your passenger access for this service has expired or been revoked.");
        }
    }
}
