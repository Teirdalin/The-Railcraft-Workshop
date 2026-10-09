[CmdletBinding()]
param([string]$Version='0.2.38',[string]$Attempt='25',[string]$BundleAudit='validation/vehicle-rail-fit-0.2.38/bundle-audit.json',[switch]$EditableVehicles,[switch]$StartupRecovery,[switch]$ManualCarts,[switch]$IncludeCamera,[int]$SavedDefinitions=929)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$audit=Join-Path $root ("validation/railworks-"+$Version)
New-Item -ItemType Directory -Path $audit -Force | Out-Null
$dll=Join-Path $root 'src/Eco.Minecarts/bin/Release/net10.0/Eco.Minecarts.dll'
$bundle=Join-Path $root '.unity-verify/minecart-client-01/Build/EcoMinecarts.unity3d'
$fixture=Join-Path $root ("validation/pulling-0.2.28/native-"+$Attempt)
$native=Get-Content -LiteralPath (Join-Path $fixture 'native-process.json') -Raw | ConvertFrom-Json
$log=Get-Content -LiteralPath (Join-Path $fixture 'native-stdout.txt') -Raw
if((Get-FileHash -LiteralPath $dll).Hash -ne $native.candidateHash){throw 'Native candidate mismatch'}
$required=if($ManualCarts){@('MANUAL_CART_KERNEL_OK:','MANUAL_MINECART_NATIVE_OK:','Server Initialization')}else{@('CORNER_CONSIST_NATIVE_OK:','VEHICLE_RAIL_FIT_NATIVE_OK:','COASTER_BRAKE_CHAIN_NATIVE_OK:','COASTER_LANDING_NATIVE_OK:','BRAKE_SPEED_ENVELOPE_NATIVE_OK:','COASTER_SLOPE_FAMILIES_NATIVE_OK:','TRAM_TERMINAL_NATIVE_OK:','SUPPORT_POWER_NATIVE_OK:','TRAM_STATION_SMOOTH_NATIVE_OK:','TRAM_CREST_NATIVE_OK:','RAILWORKS_RENAME_NATIVE_OK:','EXPORT_JUMP_NATIVE_OK:','BLUEPRINT_EXPORT_NATIVE_OK:','PULLING_NATIVE_OK:','Server Initialization')}
foreach($marker in $required){if(!$log.Contains($marker)){throw ('Native acceptance missing: '+$marker)}}
if($EditableVehicles -and !$log.Contains('VEHICLE_SETTINGS_NATIVE_OK:')){throw 'Native editable C# acceptance missing'}
if($ManualCarts -and [version]$Version -ge [version]'0.2.44' -and !$log.Contains('MIXED_CHAIN_NATIVE_OK:')){throw 'Mixed minecart/tender chain acceptance missing'}
if([version]$Version -ge [version]'0.2.44' -and !$log.Contains('SHARED_FRAMEWORK_NATIVE_OK:')){throw 'Shared vehicle framework regression acceptance missing'}
if([version]$Version -ge [version]'0.2.45' -and !$log.Contains('WHEEL_MOTION_NATIVE_OK:')){throw 'Native wheel motion acceptance missing'}
if([version]$Version -ge [version]'0.2.65' -and !$log.Contains('HANDCAR_TURN_STOP_NATIVE_OK:')){throw 'Handcar turn and dismount acceptance missing'}
if([version]$Version -ge [version]'0.2.47' -and !$log.Contains('DUMP_ANIMATION_NATIVE_OK:')){throw 'Native timed dumping acceptance missing'}
if([version]$Version -ge [version]'0.2.49' -and !$log.Contains('VEHICLE_PRESENTATION_NATIVE_OK:')){throw 'Native restraint and cab presentation acceptance missing'}
if([version]$Version -ge [version]'0.2.51' -and !$log.Contains('COASTER_CONSIST_CHAIN_NATIVE_OK:')){throw 'Coupled coaster chain traction acceptance missing'}
if([version]$Version -ge [version]'0.2.55' -and !$log.Contains('COASTER_QUEUE_NATIVE_OK:')){throw 'Independent coaster queue acceptance missing'}
if([version]$Version -ge [version]'0.2.56' -and !$log.Contains('STATION_SIGN_NATIVE_OK:')){throw 'Native station caption acceptance missing'}
if([version]$Version -ge [version]'0.2.57' -and !$log.Contains('COASTER_COUPLED_TOUCHDOWN_NATIVE_OK:')){throw 'Independent coupled coaster touchdown acceptance missing'}
if([version]$Version -ge [version]'0.2.59' -and !$log.Contains('COASTER_INDEPENDENT_AIRBORNE_NATIVE_OK:')){throw 'Independent airborne follower rotation acceptance missing'}
if([version]$Version -ge [version]'0.2.60'){
    foreach($marker in @('COASTER_LEADING_FOLLOWER_NATIVE_OK:','COASTER_STATION_POWER_NATIVE_OK:')){
        if(!$log.Contains($marker)){throw ('Coaster contact/power acceptance missing: '+$marker)}
    }
}
if([version]$Version -ge [version]'0.2.61'){
    foreach($marker in @('COASTER_OWN_CONTACT_NATIVE_OK:','COASTER_ARTICULATION_NATIVE_OK:')){
        if(!$log.Contains($marker)){throw ('Coaster spatial contact acceptance missing: '+$marker)}
    }
}
if([version]$Version -ge [version]'0.2.62' -and !$log.Contains('COASTER_STATION_RETURN_NATIVE_OK:')){throw 'Station single-use return acceptance missing'}
if([version]$Version -ge [version]'0.2.68' -and !$log.Contains('COASTER_FRONT_DOCKING_NATIVE_OK:')){throw 'Leading-car station docking acceptance missing'}
if([version]$Version -ge [version]'0.2.63' -and !$log.Contains('AUTOMATIC_TRAIN_LIGHTS_NATIVE_OK:')){throw 'Automatic train lighting acceptance missing'}
if([version]$Version -ge [version]'0.2.58'){
    foreach($marker in @('COASTER_QUEUED_LANDING_NATIVE_OK:','COASTER_JUMP_LANDING_NATIVE_OK:')){
        if(!$log.Contains($marker)){throw ('Coaster jump acceptance missing: '+$marker)}
    }
}
if([version]$Version -ge [version]'0.2.52'){
    if(!$log.Contains('VEHICLE_DESIGN_STATE_NATIVE_OK:')){throw 'Design state preservation acceptance missing'}
    $modernDefaults=if([version]$Version -ge [version]'0.2.61'){@('MineTrain','FreightLocomotive','PassengerLocomotive','LargeTrainEngine','Minecart','WoodenMinecart','RollerCoasterCart','RailroadHandcar')}else{@()}
    if([version]$Version -ge [version]'0.2.64'){$modernDefaults+=@('CoalTender','LargeCoalTender')}
    if([version]$Version -ge [version]'0.2.65'){$modernDefaults=@($modernDefaults | Where-Object {$_ -ne 'RollerCoasterCart'})}
    if([version]$Version -ge [version]'0.2.66'){$modernDefaults=@()}
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $root 'customization/Vehicles') -Filter '*.cs' -File){
        $expectedDesign=if($modernDefaults -contains $file.BaseName){'true'}else{'false'}
        if((Get-Content -LiteralPath $file.FullName -Raw) -notmatch ('\bconst\s+bool\s+NewDesign\s*=\s*'+$expectedDesign+'\s*;')){throw ('Unexpected release design default: '+$file.Name)}
    }
}
if($StartupRecovery){foreach($marker in @('COASTER_STARTUP_NATIVE_OK:','GUIDED_RESYNC_NATIVE_OK:','DUMP_RAIL_NATIVE_OK:')){if(!$log.Contains($marker)){throw "Native startup/pose/unloading acceptance missing: $marker"}}}
if($log.Contains('_NATIVE_FAILED:')){throw 'Native acceptance failed'}
$previous=Get-Content -LiteralPath (Join-Path $root $BundleAudit) -Raw | ConvertFrom-Json
if([version]$Version -ge [version]'0.2.45'){
    if($previous.vehicles -ne 14 -or $previous.animatedWheels -ne 60 -or !$previous.nativePhysicsUnchanged){throw 'Exported wheel bundle contract acceptance missing'}
    $wheelLog=Get-Content -LiteralPath (Join-Path (Split-Path (Join-Path $root $BundleAudit) -Parent) 'exported-wheels-gpu.log') -Raw
    if(!$wheelLog.Contains('EXPORTED_WHEEL_ANIMATION_OK:') -or $wheelLog.Contains('EXPORTED_WHEEL_ANIMATION_FAILED:')){throw 'Exported native wheel animation evaluation missing'}
    if([version]$Version -ge [version]'0.2.47' -and ($previous.steamEngines -ne 4 -or $previous.dumpBuckets -ne 5 -or !$wheelLog.Contains('RAIL_MECHANICAL_ANIMATION_OK:'))){throw 'Steam running gear and bucket animation acceptance missing'}
    if([version]$Version -lt [version]'0.2.66'){
        if([version]$Version -ge [version]'0.2.49' -and !$wheelLog.Contains('VEHICLE_INTEGRATION_EXPORTED_OK:')){throw 'Exported replacement model and restraint/control acceptance missing'}
        if([version]$Version -ge [version]'0.2.51' -and !$wheelLog.Contains('VEHICLE_DESIGNS_EXPORTED_OK:')){throw 'Exported modern and legacy vehicle design acceptance missing'}
    }elseif(!$wheelLog.Contains('ORIGINAL_VEHICLES_EXPORTED_OK:')){throw 'Original-only vehicle release acceptance missing'}
    if([version]$Version -ge [version]'0.2.67' -and !$wheelLog.Contains('ORIGINAL_COASTER_RESTRAINTS_EXPORTED_OK:')){throw 'Original coaster visible restraint animation acceptance missing'}
    if([version]$Version -ge [version]'0.2.54' -and !$wheelLog.Contains('PRESENTATION_REPAIRS_EXPORTED_OK:')){throw 'Exported tram lamp and presentation repair acceptance missing'}
    if([version]$Version -ge [version]'0.2.56' -and !$wheelLog.Contains('STATION_SIGN_EXPORTED_OK:')){throw 'Station track, cabinet and caption export acceptance missing'}
    if([version]$Version -lt [version]'0.2.66'){
        if([version]$Version -ge [version]'0.2.62' -and !$wheelLog.Contains('VEHICLE_FIT_REPAIRS_EXPORTED_OK:')){throw 'Vehicle cab, coach roof and handcar linkage acceptance missing'}
        if([version]$Version -ge [version]'0.2.64' -and !$wheelLog.Contains('COASTER_SEAT_BAY_EXPORTED_OK:')){throw 'Coaster chair interior clearance acceptance missing'}
    }
    if([version]$Version -ge [version]'0.2.65'){
        if([version]$Version -lt [version]'0.2.66' -and !$wheelLog.Contains('HANDCAR_GRIPS_EXPORTED_OK:')){throw 'Handcar animated grip alignment acceptance missing'}
        $cameraCheck=Join-Path (Split-Path (Join-Path $root $BundleAudit) -Parent) 'camera-checks.log'
        if(!(Test-Path -LiteralPath $cameraCheck) -or !(Get-Content -LiteralPath $cameraCheck -Raw).Contains('COASTER_CAMERA_SEAT_ANCHOR_OK:')){throw 'Occupied coaster seat camera anchor acceptance missing'}
    }
}
if((Get-FileHash -LiteralPath $bundle).Hash -ne $previous.bundleHash){throw 'Bundle differs from its verified asset audit'}
$guard=Join-Path $root 'tools/ReleaseGuard/bin/Release/net10.0/ReleaseGuard.exe'
& $guard --compatible-schema (Join-Path $root 'dist/EcoMinecarts-0.2.29/Eco.Minecarts.dll') $dll
if($LASTEXITCODE -ne 0){throw 'Saved schema changed'}
& $guard $dll
if($LASTEXITCODE -ne 0){throw 'Production guard failed'}
$stage=Join-Path $root ("dist/Railworks-Workshop-Server-"+$Version)
$zip=Join-Path $root ("dist/Railworks-Workshop-Server-"+$Version.Replace('.','-')+'.zip')
$desktop=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) ("Railworks-Workshop-Server-"+$Version.Replace('.','-')+'.zip')
foreach($path in @($stage,$zip,$desktop)){if(Test-Path -LiteralPath $path){throw ('Preserve existing release: '+$path)}}
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $stage 'Railworks.dll')
Copy-Item -LiteralPath $bundle -Destination (Join-Path $stage 'Railworks.unity3d')
if($EditableVehicles){
    $vehicleFiles=@(Get-ChildItem -LiteralPath (Join-Path $root 'customization/Vehicles') -Filter '*.cs' -File)
    if($vehicleFiles.Count -ne 14){throw 'Expected fourteen editable vehicle files'}
    New-Item -ItemType Directory -Path (Join-Path $stage 'Vehicles') | Out-Null
    foreach($file in $vehicleFiles){Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $stage 'Vehicles')}
}
if([version]$Version -ge [version]'0.2.63'){
    if([version]$Version -lt [version]'0.2.66' -and !$wheelLog.Contains('RAIL_VEHICLE_63_EXPORTED_OK:')){throw 'Coaster scale, engine proportion and lighting acceptance missing'}
}
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
    $files=@($archive.Entries | Where-Object {!$_.FullName.EndsWith('/')})
    $expected=$(if($EditableVehicles){16}else{2})
    if($files.Count -ne $expected -or !($files.FullName -contains 'Railworks.dll') -or !($files.FullName -contains 'Railworks.unity3d')){throw 'Unexpected distribution files'}
    if($EditableVehicles -and @($files | Where-Object {$_.FullName -like 'Vehicles/*.cs'}).Count -ne 14){throw 'Missing editable vehicles'}
}finally{$archive.Dispose()}
Copy-Item -LiteralPath $zip -Destination $desktop
@{version=$Version;name='Railworks Workshop';dllHash=$native.candidateHash;bundleHash=$previous.bundleHash;zipHash=(Get-FileHash -LiteralPath $desktop).Hash;savedSchema=$SavedDefinitions;nativeVerified=$true;liveRenderingVerified=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $audit 'stage.json')
Write-Output ('RAILWORKS_STAGED: '+$desktop)
if($IncludeCamera){
    $camera=Join-Path $root 'development/Eco.Railcraft.CoasterCamera.Client/bin/Release/net6.0/Eco.Railcraft.CoasterCamera.Client.dll'
    if(!(Test-Path -LiteralPath $camera)){throw 'Build the optional camera client first'}
    $cameraVersion=[Reflection.AssemblyName]::GetAssemblyName($camera).Version.ToString(3)
    $cameraName='Railworks-Workshop-BepInEx-Camera-'+$cameraVersion.Replace('.','-')
    $cameraStage=Join-Path $root ('dist/'+$cameraName)
    $cameraZip=Join-Path $root ('dist/'+$cameraName+'.zip')
    $cameraDesktop=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) ($cameraName+'.zip')
    foreach($path in @($cameraStage,$cameraZip,$cameraDesktop)){if(Test-Path -LiteralPath $path){throw ('Preserve existing camera package: '+$path)}}
    New-Item -ItemType Directory -Path (Join-Path $cameraStage 'BepInEx/plugins') | Out-Null
    Copy-Item -LiteralPath $camera -Destination (Join-Path $cameraStage 'BepInEx/plugins')
    Copy-Item -LiteralPath (Join-Path $root 'development/Eco.Railcraft.CoasterCamera.Client/README.md') -Destination (Join-Path $cameraStage 'README.md')
    Compress-Archive -Path (Join-Path $cameraStage '*') -DestinationPath $cameraZip
    Copy-Item -LiteralPath $cameraZip -Destination $cameraDesktop
    @{version=$cameraVersion;name='Railworks Workshop optional BepInEx camera';pluginHash=(Get-FileHash -LiteralPath $camera).Hash;zipHash=(Get-FileHash -LiteralPath $cameraDesktop).Hash;requiresLocalLoader=$true;installedOnJoin=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $audit 'camera-stage.json')
    Write-Output ('RAILWORKS_CAMERA_STAGED: '+$cameraDesktop)
}
