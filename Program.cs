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
            string rgName = Utilities.CreateRandomName("rg");
            string networkWatcherName = Utilities.CreateRandomName("watcher");
            string vnetName = Utilities.CreateRandomName("vnet");
            string nsgName = Utilities.CreateRandomName("nsg");
            string dnsLabel = Utilities.CreateRandomName("pipdns");
            string saName = Utilities.CreateRandomName("sa");
            string vmName = Utilities.CreateRandomName("vm");
            string packetCaptureName = Utilities.CreateRandomName("pc");
            NetworkWatcherResource networkWatcher = null;

            try
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
                ICreatable<INetwork> virtualNetwork = azure.Networks.Define(vnetName)
                    .WithRegion(region)
                    .WithExistingResourceGroup(rgName)
                    .WithAddressSpace("192.168.0.0/16")
                    .DefineSubnet(subnetName)
                        .WithAddressPrefix("192.168.2.0/24")
                        .WithExistingNetworkSecurityGroup(nsg)
                        .Attach();
                Utilities.Log("Creating virtual machine...");
                IVirtualMachine vm = azure.VirtualMachines.Define(vmName)
                    .WithRegion(region)
                    .WithExistingResourceGroup(rgName)
                    .WithNewPrimaryNetwork(virtualNetwork)
                    .WithPrimaryPrivateIPAddressDynamic()
                    .WithNewPrimaryPublicIPAddress(dnsLabel)
                    .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.UbuntuServer14_04_Lts)
                    .WithRootUsername(userName)
                    .WithRootPassword("Abcdef.123456")
                    .WithSize(VirtualMachineSizeTypes.Parse("Standard_D2a_v4"))
                    .DefineNewExtension("packetCapture")
                        .WithPublisher("Microsoft.Azure.NetworkWatcher")
                        .WithType("NetworkWatcherAgentLinux")
                        .WithVersion("1.4")
                        .WithMinorVersionAutoUpgrade()
                        .Attach()
                    .Create();

                // Create storage account
                Utilities.Log("Creating storage account...");
                IStorageAccount storageAccount = azure.StorageAccounts.Define(saName)
                    .WithRegion(region)
                    .WithExistingResourceGroup(rgName)
                    .Create();

                // Start a packet capture
                Utilities.Log("Creating packet capture...");
                IPacketCapture packetCapture = nw.PacketCaptures
                    .Define(packetCaptureName)
                    .WithTarget(vm.Id)
                    .WithStorageAccountId(storageAccount.Id)
                    .WithTimeLimitInSeconds(1500)
                    .DefinePacketCaptureFilter()
                        .WithProtocol(PcProtocol.TCP)
                        .Attach()
                    .Create();
                Utilities.Log("Created packet capture");
                Utilities.Print(packetCapture);

                // Stop a packet capture
                Utilities.Log("Stopping packet capture...");
                packetCapture.Stop();
                Utilities.Print(packetCapture);

                // Get a packet capture
                Utilities.Log("Getting packet capture...");
                IPacketCapture packetCapture1 = nw.PacketCaptures.GetByName(packetCaptureName);
                Utilities.Print(packetCapture1);

                // Delete a packet capture
                Utilities.Log("Deleting packet capture");
                nw.PacketCaptures.DeleteByName(packetCapture.Name);

                //============================================================
                // Verify IP flow – verify if traffic is allowed to or from a virtual machine
                // Get the IP address of a NIC on a virtual machine
                String ipAddress = vm.GetPrimaryNetworkInterface().PrimaryPrivateIP;
                // Test IP flow on the NIC
                Utilities.Log("Verifying IP flow for vm id " + vm.Id + "...");
                IVerificationIPFlow verificationIPFlow = nw.VerifyIPFlow()
                    .WithTargetResourceId(vm.Id)
                    .WithDirection(Direction.Outbound)
                    .WithProtocol(IpFlowProtocol.TCP)
                    .WithLocalIPAddress(ipAddress)
                    .WithRemoteIPAddress("8.8.8.8")
                    .WithLocalPort("443")
                    .WithRemotePort("443")
                    .Execute();
                Utilities.Print(verificationIPFlow);

                //============================================================
                // Analyze next hop – get the next hop type and IP address for a virtual machine
                Utilities.Log("Calculating next hop...");
                INextHop nextHop = nw.NextHop().WithTargetResourceId(vm.Id)
                    .WithSourceIPAddress(ipAddress)
                    .WithDestinationIPAddress("8.8.8.8")
                    .Execute();
                Utilities.Print(nextHop);

                //============================================================
                // Retrieve network topology for a resource group
                Utilities.Log("Getting topology...");
                ITopology topology = nw.Topology()
                    .WithTargetResourceGroup(rgName)
                    .Execute();
                Utilities.Print(topology);

                //============================================================
                // Analyze Virtual Machine Security by examining effective network security rules applied to a VM
                // Get security group view for the VM
                Utilities.Log("Getting security group view for a vm");
                ISecurityGroupView sgViewResult = nw.GetSecurityGroupView(vm.Id);
                Utilities.Print(sgViewResult);

                //============================================================
                // Configure Network Security Group Flow Logs

                // Get flow log settings
                IFlowLogSettings flowLogSettings = nw.GetFlowLogSettings(nsg.Id);
                Utilities.Print(flowLogSettings);

                // Enable NSG flow log
                flowLogSettings.Update()
                    .WithLogging()
                    .WithStorageAccount(storageAccount.Id)
                    .WithRetentionPolicyDays(5)
                    .WithRetentionPolicyEnabled()
                    .Apply();
                Utilities.Print(flowLogSettings);

                // Disable NSG flow log
                flowLogSettings.Update()
                    .WithoutLogging()
                    .Apply();
                Utilities.Print(flowLogSettings);

                //============================================================
                // Delete network watcher
                Utilities.Log("Deleting network watcher");
                azure.NetworkWatchers.DeleteById(nw.Id);
                Utilities.Log("Deleted network watcher");
            }
            finally
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
            try
            {
                //=================================================================
                // Authenticate
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);

                await RunSample(client);
            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
            }
        }
    }
}