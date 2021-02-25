# Ingress Controller Prerequisites

The AKS Cluster has been enrolled in [GitOps management](./06-gitops.md), wrapping up the infrastructure focus of the [AKS secure Baseline reference implementation](./). Follow the steps below to import the TLS certificate that the Ingress Controller will serve for Application Gateway to connect to your web app.

This point in the steps marks a significant transition in roles and purpose. At this point, you have a AKS cluster that is deployed in an architecture that will help your compliance needs and is bootstrapped with core additions you feel are requirements for your solution, all managed via Flux. A cluster without any business workloads, essentially. The next few steps will walk through considerations that are specific to the first workload in the cluster. Workloads are a mix of potential infrastructure changes (e.g. Azure Application Gateway routes, Azure Resources for the workload itself -- such as CosmosDB for state storage and Azure Cache for Redis for cache.), privileged cluster changes (i.e. creating target namespace, creating and assigning, any specific cluster or namespace roles, etc.), deciding on how that "last mile" deployment of these workloads will be handled (e.g. using the ops subnet adjacent to this cluster), and workload teams which are responsible for creating the container image(s), building deployment manifests, etc. Many regulations have a clear separation of duties requirements, be sure in your case you have documented and understood change management process. How you partition this work will not be described here because there isn't a one-size-fits-most solution. Allocate time to plan, document, and educate on these concerns.

## Expected results

* A wildcard TLS certificate (`*.aks-ingress.contoso.com`) is imported into Azure Key Vault that will be used by your workload's ingress controller to expose an HTTPS endpoint to Azure Application Gateway.
* A Pod Managed Identity (`podmi-ingress-controller`) is deployed to the `a0005` namespace and ready to be bound via the name `podmi-ingress-controller`.
* The same managed identity is granted the ability to pull the TLS certificate from Key Vault.
* The ingress controller image is imported into ACR (after passing through quarantine).

## Import the wildcard certificate for the AKS Ingress Controller to Azure Key Vault

Once traffic hits Azure Application Gateway, public-facing TLS is terminated. This supports WAF inspection rules and other request manipulation features of Azure Application Gateway. The next hop for this traffic is to the internal load balancer and then to the in-cluster ingress controller. Starting at Application Gateway, all subsequent network hops are done via your private virtual network and are no longer traversing public networks. That said, we still desire to provide TLS as an added layer of protection when traversing between Azure Application Gateway and our ingress controller. That'll bring TLS encryption _into_ your cluster from Application Gateway. We'll address pod-to-pod encryption later.

### Steps

1. Give your user permissions to import certificates.

   TODO: Can this be done via role assignment instead of policy.  I know we have policy in the ARM template, not sure if you can "mix and match..."

   ```bash
   KEYVAULT_NAME=$(az deployment group show --resource-group rg-bu0001a0005 -n cluster-stamp --query properties.outputs.keyVaultName.value -o tsv)
   az keyvault set-policy --certificate-permissions import list get --upn $(az account show --query user.name -o tsv) -n $KEYVAULT_NAME
   ```

1. Import the AKS Ingress Controller's certificate.

   ```bash
   cat traefik-ingress-internal-aks-ingress-contoso-com-tls.crt traefik-ingress-internal-aks-ingress-contoso-com-tls.key > traefik-ingress-internal-aks-ingress-contoso-com-tls.pem
   az keyvault certificate import -f traefik-ingress-internal-aks-ingress-contoso-com-tls.pem -n traefik-ingress-internal-aks-ingress-contoso-com-tls --vault-name $KEYVAULT_NAME
   ```

1. Remove Azure Key Vault import certificates permissions for current user.

   > The Azure Key Vault Policy for your user was a temporary policy to allow you to upload the certificate for this walkthrough. In actual deployments, you would manage these access policies via your ARM templates using [Azure RBAC for Key Vault data plane](https://docs.microsoft.com/azure/key-vault/general/secure-your-key-vault#data-plane-and-access-policies).

   ```bash
   az keyvault delete-policy --upn $(az account show --query user.name -o tsv) -n $KEYVAULT_NAME
   ```

## Setting up Pod Managed Identity

Pod Managed Identity goes beyond being just a workload concern. A Pod Managed Identity is a User Managed Identity resource in Azure, and needs to be managed like any other resource in Azure. Likewise, the cluster's control plane Managed Identity must have the authority to assign this identity to the nodepools (VMSS resources). As such, before a workload can use a managed identity the cluster operator needs to make it "available" to the workload. In this point in the walkthrough, we are going to deploy a slight variant of the prior cluster stamp ARM template that will associated a User Managed Identity with the cluster, assign it permissions to read the certificate you just imported above, and make it available for your workload. Because Pod Managed Identity is installed as a cluster add-on (vs through the manual open source installation option), it is critical that all pod identities are managed via the cluster's ARM template. **Mixing imperative management of identities (`az aks pod-identity ...` or `kubectl`) and declarative management of identities (ARM/Terraform template) will lead to an unexpected removal of identities, which will cause an outage in your workload.**

### Steps

TODO: "api" -> "a0005" everywhere.
TODO: Should we defer setting up real routes until this point?

1. Deploy the cluster ARM template that has been updated with the Pod Managed Identity assignment.

   This is a small evolution of the cluster-stamp.json ARM template you deployed before, but with a couple of updates to include the Pod Managed Identity that needs to be made available to the workload. You'll be using the exact same parameters as before. If you wish to see the difference between these two infrastructure as code templates, you can diff them HERE (TODO).

   ```bash
   # [This takes about 10 minutes.]
   az deployment group create -g rg-bu0001a0005 -f cluster-stamp.v1.json -p targetVnetResourceId=${RESOURCEID_VNET_CLUSTERSPOKE} clusterAdminAadGroupObjectId=${AADOBJECTID_GROUP_CLUSTERADMIN} k8sControlPlaneAuthorizationTenantId=${TENANTID_K8SRBAC} appGatewayListenerCertificate=${APP_GATEWAY_LISTENER_CERTIFICATE} aksIngressControllerCertificate=${AKS_INGRESS_CONTROLLER_CERTIFICATE_BASE64} jumpBoxImageResourceId=${RESOURCEID_IMAGE_JUMPBOX} jumpBoxCloudInitAsBase64=${CLOUDINIT_BASE64}

   # Or if you used the parameters file...
   #az deployment group create -g rg-bu0001a0005 -f cluster-stamp.v1.json -p "@azuredeploy.parameters.prod.json"
   ```

1. _From your Azure Bastion connection_, confirm your Pod Managed Identity now exists in your cluster.

   ```bash
   kubectl describe AzureIdentity,AzureIdentityBinding -n a0005
   ```

   This will show you the Azure identity kubernetes resources that were created via the ARM template changes applied in the prior step. This means that any workload in the `a0005` namespace that wishes to identify itself as the Azure resource `podmi-ingress-controller` can do so by adding a `aadpodidbinding: podmi-ingress-controller` label to their pod deployment. In this walkthrough, our ingress controller, Traefik, will be using that identity, combined with the Secret Store driver for Key Vault to pull the TLS certificate you imported above.

## Import ingress controller image to ACR

### Steps

1. Quarantine your ingress controller image.

   ```bash
   az acr import --source docker.io/library/traefik:2.4.5 -t quarantine/library/traefik:2.4.5 -n $ACR_NAME_QUARANTINE
   ```

   In this case, for simplicity, we are using the single ACR instance that you deployed with your cluster. Workload teams may opt to use their own dedicated ACR instances, separate from the bootstrap instance. Consider your options here wrt Azure Policy validation, centralized view of container scanning, image signing concerns, geo-replication, cost, etc. The same level of protection for bootstrap images must be enforced for workload images as well.

1. Release the ingress controller image from quarantine.

   ```bash
   az acr import --source quarantine/library/traefik:2.4.5 -r $ACR_NAME_QUARANTINE -t live/library/traefik:2.4.5 -n $ACR_NAME
   ```

### Who manages the ingress controller

You'll need to decide how to consider the management of ingresses and ingress controllers. You might opt for a single, shared ingress controller for all work in your cluster, keeping workloads one step removed from this concern. In that case, your cluster bootstrapping might have included some or all of these steps -- and your ingress controller might be looking at a wider set of namespaces for ingress requests. Alternatively, you could opt to have workloads take care of all of their concerns, including ingress controllers. For clusters that contain a single, unified workload, the distinction might not be that great between the two. Ensure you've discussed the pros and cons with your team and documented your decisions on this. Since regulated clusters should be as narrow in scope as practical, consider promoting the management of the ingress controller to be a bootstrap concern.

TODO: Do we have enough time to pull this out and configure it as such?

## Next step

:arrow_forward: [Configure AKS Ingress Controller with Azure Key Vault integration](./13-secret-managment-and-ingress-controller.md)
