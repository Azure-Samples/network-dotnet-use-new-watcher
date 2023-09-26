// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Resources.Models;
using Azure.ResourceManager.Samples.Common;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Storage.Models;
using Azure.ResourceManager.Storage;
using Microsoft.Identity.Client.Extensions.Msal;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Compute;


namespace ManageNetworkWatcher
{
    public class Program
    {
        private static ResourceIdentifier? _resourceGroupId = null;

        /**
         * Azure Network sample for managing network watcher -
         *  - Create Network Watcher
         *	- Manage packet capture – track traffic to and from a virtual machine
         *   	Create a VM
         *      Start a packet capture
         *      Stop a packet capture
         *      Get a packet capture
         *      Delete a packet capture
         *  - Verify IP flow – verify if traffic is allowed to or from a virtual machine
         *      Get the IP address of a NIC on a virtual machine
         *      Test IP flow on the NIC
         *  - Analyze next hop – get the next hop type and IP address for a virtual machine
         *  - Retrieve network topology for a resource group
         *  - Analyze Virtual Machine Security by examining effective network security rules applied to a VM
         *      Get security group view for the VM
         *  - Configure Network Security Group Flow Logs
         *      Get flow log settings
         *      Enable NSG flow log
         *      Disable NSG flow log
         *  - Delete network watcher
         */
        public static async Task RunSample(ArmClient client)
        {
            string rgName = Utilities.CreateRandomName("NetworkSampleRG");
            string networkWatcherName = Utilities.CreateRandomName("watcher");
            string vnetName = Utilities.CreateRandomName("vnet");
            string nsgName = Utilities.CreateRandomName("nsg");
            string storageAccountName = Utilities.CreateRandomName("azstorageaccount");
            string vmName = Utilities.CreateRandomName("vm");
            string packetCaptureName = Utilities.CreateRandomName("packetCapture");
            NetworkWatcherResource networkWatcher = null;

            {
                // Get default subscription
                SubscriptionResource subscription = await client.GetDefaultSubscriptionAsync();

                // Create a resource group in the EastUS region
                Utilities.Log($"Creating resource group...");
                ArmOperation<ResourceGroupResource> rgLro = await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, rgName, new ResourceGroupData(AzureLocation.WestUS));
                ResourceGroupResource resourceGroup = rgLro.Value;
                _resourceGroupId = resourceGroup.Id;
                Utilities.Log("Created a resource group with name: " + resourceGroup.Data.Name);

                //============================================================
                // Create network watcher
                Utilities.Log($"Create a network watcher in {resourceGroup.Data.Location}...");
                Utilities.Log("To note: one subscription only has a Network Watcher in the same region");
                Utilities.Log($"         make sure there has not Network Watcher in {resourceGroup.Data.Location}");
                NetworkWatcherData networkWatcherInput = new NetworkWatcherData()
                {
                    Location = resourceGroup.Data.Location,
                };
                var networkWatcherLro = await resourceGroup.GetNetworkWatchers().CreateOrUpdateAsync(WaitUntil.Completed, networkWatcherName, networkWatcherInput);
                networkWatcher = networkWatcherLro.Value;
                Utilities.Log($"Created network watcher: {networkWatcher.Data.Name}");

                //============================================================
                // Manage packet capture – track traffic to and from a virtual machine

                // Create network security group, virtual network and VM; add packetCapture extension to enable packet capture
                Utilities.Log("Creating network security group...");
                NetworkSecurityGroupData nsgInput = new NetworkSecurityGroupData()
                {
                    Location = resourceGroup.Data.Location,
                    SecurityRules =
                    {
                        new SecurityRuleData()
                        {
                            Name = "DenyInternetInComing",
                            Description = "Allow SQL",
                            Access = SecurityRuleAccess.Deny,
                            Direction = SecurityRuleDirection.Inbound,
                            SourceAddressPrefix = "*",
                            SourcePortRange = "*",
                            DestinationAddressPrefix = "*",
                            DestinationPortRange = "443",
                            Priority = 100,
                            Protocol = SecurityRuleProtocol.Tcp,
                        },
                    }
                };
                var nsgLro = await resourceGroup.GetNetworkSecurityGroups().CreateOrUpdateAsync(WaitUntil.Completed, nsgName, nsgInput);
                NetworkSecurityGroupResource nsg = nsgLro.Value;
                Utilities.Log("Created a security group for the front end: " + nsg.Data.Name);

                Utilities.Log("Creating virtual network...");
                VirtualNetworkData vnetInput = new VirtualNetworkData()
                {
                    Location = resourceGroup.Data.Location,
                    AddressPrefixes = { "192.168.0.0/16" },
                    Subnets =
                    {
                        new SubnetData() { AddressPrefix = "192.168.2.0/24", Name = "default" , NetworkSecurityGroup = nsg.Data},
                    },
                };
                var vnetLro = await resourceGroup.GetVirtualNetworks().CreateOrUpdateAsync(WaitUntil.Completed, vnetName, vnetInput);
                VirtualNetworkResource vnet = vnetLro.Value;
                Utilities.Log($"Created a virtual network: {vnet.Data.Name}");

                Utilities.Log("Creating virtual machine...");
                // Definate vm extension input data
                string extensionName = "packetCapture";
                var extensionInput = new VirtualMachineExtensionData(resourceGroup.Data.Location)
                {
                    Publisher = "Microsoft.Azure.NetworkWatcher",
                    ExtensionType = "NetworkWatcherAgentWindows",
                    TypeHandlerVersion = "1.4",
                    AutoUpgradeMinorVersion = true,
                };
                // Create vm
                NetworkInterfaceResource nic = await Utilities.CreateNetworkInterface(resourceGroup, vnet);
                VirtualMachineData vmInput = Utilities.GetDefaultVMInputData(resourceGroup, vmName);
                vmInput.NetworkProfile.NetworkInterfaces.Add(new VirtualMachineNetworkInterfaceReference() { Id = nic.Id, Primary = true });
                var vmLro = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, vmName, vmInput);
                VirtualMachineResource vm = vmLro.Value;
                _ = await vm.GetVirtualMachineExtensions().CreateOrUpdateAsync(WaitUntil.Completed, extensionName, extensionInput);
                Utilities.Log($"Created vm: {vm.Data.Name}");

                // Create storage account
                Utilities.Log("Creating storage account...");
                StorageSku storageSku = new StorageSku(StorageSkuName.StandardGrs);
                StorageKind storageKind = StorageKind.Storage;
                StorageAccountCreateOrUpdateContent storagedata = new StorageAccountCreateOrUpdateContent(storageSku, storageKind, resourceGroup.Data.Location) { };
                var storageAccountLro = await resourceGroup.GetStorageAccounts().CreateOrUpdateAsync(WaitUntil.Completed, storageAccountName, storagedata);
                StorageAccountResource storageAccount = storageAccountLro.Value;
                Utilities.Log($"Created storage account: {storageAccount.Data.Name}");

                // Start a packet capture
                Utilities.Log("Creating packet capture...");
                PacketCaptureStorageLocation packetCaptureStorageLocation = new PacketCaptureStorageLocation()
                {
                    StorageId = storageAccount.Id,
                };
                PacketCaptureCreateOrUpdateContent packetCaptureInput = new PacketCaptureCreateOrUpdateContent(vm.Id.ToString(), packetCaptureStorageLocation)
                {
                    TimeLimitInSeconds = 1500,
                    Filters = { new PacketCaptureFilter() { Protocol = PcProtocol.Tcp } }
                };
                var packetCaptureLro = await networkWatcher.GetPacketCaptures().CreateOrUpdateAsync(WaitUntil.Completed, packetCaptureName, packetCaptureInput);
                PacketCaptureResource packetCapture = packetCaptureLro.Value;
                Utilities.Log($"Created packet capture: {packetCapture.Data.Name}");

                // Stop a packet capture
                Utilities.Log("Stopping packet capture...");
                await packetCapture.StopAsync(WaitUntil.Completed);

                // Get a packet capture
                Utilities.Log("Getting packet capture...");
                var getPackketCaptureLro = await networkWatcher.GetPacketCaptures().GetAsync(packetCaptureName);
                PacketCaptureResource getPackketCapture = getPackketCaptureLro.Value;

                // Delete a packet capture
                Utilities.Log("Deleting packet capture");
                await packetCapture.DeleteAsync(WaitUntil.Completed);

                //============================================================
                // Verify IP flow – verify if traffic is allowed to or from a virtual machine
                // Get the IP address of a NIC on a virtual machine
                string ipAddress = nic.Data.IPConfigurations.First().PrivateIPAddress;
                // Test IP flow on the NIC
                Utilities.Log("Verifying IP flow for vm id " + vm.Id + "...");
                VerificationIPFlowContent verificationIPFlowInput = new VerificationIPFlowContent(
                    targetResourceId: vm.Id,
                    direction: NetworkTrafficDirection.Outbound,
                    protocol: IPFlowProtocol.Tcp,
                    localPort: "443",
                    remotePort: "443",
                    localIPAddress: ipAddress,
                    remoteIPAddress: "8.8.8.8"
                    );
                var verificationIPFlowResult = await networkWatcher.VerifyIPFlowAsync(WaitUntil.Completed, verificationIPFlowInput);
                Utilities.Log("Access: " + verificationIPFlowResult.Value.Access);
                Utilities.Log("RuleName: " + verificationIPFlowResult.Value.RuleName);

                //============================================================
                // Analyze next hop – get the next hop type and IP address for a virtual machine
                Utilities.Log("Calculating next hop...");
                NextHopContent nextHopContent = new NextHopContent(
                    targetResourceId: vm.Id,
                    sourceIPAddress: ipAddress,
                    destinationIPAddress: "8.8.8.8");
                var nextHopResult = await networkWatcher.GetNextHopAsync(WaitUntil.Completed, nextHopContent);
                Utilities.Log("NextHopType: " + nextHopResult.Value.NextHopType);

                //============================================================
                // Retrieve network topology for a resource group
                Utilities.Log("Getting topology...");
                TopologyContent topologyContent = new TopologyContent();
                var topologyResult = await networkWatcher.GetTopologyAsync(topologyContent);
                Utilities.Log("Resources count: " + topologyResult.Value.Resources.Count);

                //============================================================
                // Analyze Virtual Machine Security by examining effective network security rules applied to a VM
                // Get security group view for the VM
                Utilities.Log("Getting security group view for a vm");
                SecurityGroupViewContent securityGroupViewContent = new SecurityGroupViewContent(vm.Id);
                var vmSecurityRulesResult = await networkWatcher.GetVmSecurityRulesAsync(WaitUntil.Completed, securityGroupViewContent);
                Utilities.Log("NetworkInterfaces count: " + vmSecurityRulesResult.Value.NetworkInterfaces.Count);

                //============================================================
                // Configure Network Security Group Flow Logs

                // Get flow log settings
                FlowLogInformation flowLogInformationInput = new FlowLogInformation(
                    targetResourceId: nsg.Id,
                    storageId: storageAccount.Id,
                    enabled: true);
                var flowLogLroInformation = await networkWatcher.SetFlowLogConfigurationAsync(WaitUntil.Completed, flowLogInformationInput);
                FlowLogInformation flowLogInformation = flowLogLroInformation.Value;
                Utilities.Log("TargetResourceId: " + flowLogInformation.TargetResourceId);
                Utilities.Log("Enabled: " + flowLogInformation.Enabled);

                // Enable NSG flow log
                string flowLogName = "flowlog";
                FlowLogData flowLogInput = new FlowLogData()
                {
                    TargetResourceId = nsg.Id,
                    StorageId = storageAccount.Id,
                    RetentionPolicy = new RetentionPolicyParameters() { Days = 5, Enabled = true },
                    Enabled = true,
                };
                var flowLogLro = await networkWatcher.GetFlowLogs().CreateOrUpdateAsync(WaitUntil.Completed, flowLogName, flowLogInput);
                var flowlog = flowLogLro.Value;
                Utilities.Log("Enabled" + flowlog.Data.Enabled);

                // Disable NSG flow log
                flowLogInput = flowlog.Data;
                flowLogInput.Enabled = false;
                flowLogLro = await networkWatcher.GetFlowLogs().CreateOrUpdateAsync(WaitUntil.Completed, flowLogName, flowLogInput);
                flowlog = flowLogLro.Value;
                Utilities.Log("Enabled" + flowlog.Data.Enabled);

                //============================================================
                // Delete network watcher
                Utilities.Log("Deleting network watcher");
                await networkWatcher.DeleteAsync(WaitUntil.Completed);
                Utilities.Log("Deleted network watcher");
            }
            {
                try
                {
                    if (networkWatcher != null)
                    {
                        Utilities.Log($"Deleting network watcher...");
                        await networkWatcher.DeleteAsync(WaitUntil.Completed);
                        Utilities.Log($"Deleted network watcher {networkWatcher.Data.Name}");
                    }
                    if (_resourceGroupId is not null)
                    {
                        Utilities.Log($"Deleting Resource Group...");
                        await client.GetResourceGroupResource(_resourceGroupId).DeleteAsync(WaitUntil.Completed);
                        Utilities.Log($"Deleted Resource Group: {_resourceGroupId.Name}");
                    }
                }
                catch (NullReferenceException)
                {
                    Utilities.Log("Did not create any resources in Azure. No clean up is necessary");
                }
                catch (Exception ex)
                {
                    Utilities.Log(ex);
                }
            }
        }

        public static async Task Main(string[] args)
        {
            var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
            var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
            var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
            ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            ArmClient client = new ArmClient(credential, subscription);

            await RunSample(client);
            try
            {
                //=================================================================
                // Authenticate

            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
            }
        }
    }
}