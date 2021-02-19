# Place the Cluster Under GitOps Management

Now that [the AKS cluster](./09-aks-cluster.md) has been deployed, and your [bootstrapping images have passed through quarantine](./10-registry-quarantine.md), the next step to configure a GitOps management solution on our cluster, Flux in this case.

## Expected results

### Jump box access is validated

While the following process likely would be handled via your deployment pipelines, we are going to use this opportunity to demonstrate cluster management access via Azure Bastion, and show that your cluster cannot be directly accessed locally.

### Flux is configured and deployed

Your github repo will be the source of truth for your cluster's configuration. Typically this would be a private repo, but for ease of demonstration, it'll be connected to a public repo (all firewall permissions are set to allow this specific interaction.) You'll be updating a configuration resource for Flux so that it knows to point to your own repo.

## Steps

1. Update kustomization files to use images from your container registry.

   ```bash
   cd cluster-manifests
   grep -lr REPLACE_ME_WITH_YOUR_ACRNAME --include=kustomization.yaml | xargs sed -i "s/REPLACE_ME_WITH_YOUR_ACRNAME/${ACR_NAME}/g"

   git commit -a -m "Update bootstrap deployments to use images from my ACR instead of public container registries."
   ```

1. Update flux to pull from your repo instead of the mspnp repo.

   ```bash
   sed -i "s/REPLACE_ME_WITH_YOUR_GITHUBACCOUNTNAME/${GITHUB_ACCOUNT_NAME}/" flux-system/gotk-sync.yaml

   git commit -a -m "Update Flux to pull from my fork instead of the upstream Microsoft repo."
   ```

1. Push those two changes to your repo.

   ```bash
   git push
   ```

1. Connect to a jump box node via Azure Bastion.

   If this is the first time you've used Azure Bastion, here is a detailed walk through of this process.

   1. Open the [Azure Portal](https://portal.azure.com).
   2. Navigate to the **rg-bu0001a0005** resource group.
   3. Click on the Virtual Machine Scale Set resource named **vmss-jumpboxes**.
   4. Click **Instances**.
   5. Click the name of any of the two listed instances. E.g. **vmss-jumpboxes_0**
   6. Click **Connect** -> **Bastion** -> **Use Bastion**.
   7. Fill in the username field with one of the users from your customized `jumpBoxCloudInit.yml` file. E.g. **opsuser01**
   8. Select **SSH Private Key from Local File** and select your private key file for that specific user.
   9. Provide your SSH passphrase in **SSH Passphrase** if your private key is protected with one.
   10. Click **Connect**.
   11. For enhanced "copy-on-select" & "paste-on-right-click" support, your browser may request your permission to support those features. It's recommended that you _Allow_ that feature. If you don't, you'll have to use the **>>** flyout on the screen to perform copy & paste actions.
   12. Welcome to your jump box!

1. From your Azure Bastion connection, log into your Azure RBAC tenant and select your subscription.

   The following command will perform a device login. Ensure you're logging in with the Azure AD user that has access to your AKS resources (i.e. the one you did your deployment with.)

   ```bash
   az login
   # This will give you a link to https://microsoft.com/devicelogin where you can enter 
   # the provided code and perform authentication.

   # Ensure you're on the correct subscription
   az account show

   # If not, select the correct subscription
   # az account set -s <subscription name or id>
   ```

1. _From your Azure Bastion connection_, get your AKS credentials and set your `kubectl` context to your cluster.

   ```bash
   AKS_CLUSTER_NAME=$(az deployment group show -g rg-bu0001a0005 -n cluster-stamp --query properties.outputs.aksClusterName.value -o tsv)

   az aks get-credentials -g rg-bu0001a0005 -n $AKS_CLUSTER_NAME
   ```

1. _From your Azure Bastion connection_, test cluster access and authenticate as a cluster admin user.

   The following command will force you to authenticate into your AKS cluster's control plane. This will start yet another device login flow. For this one (**Azure Kubernetes Service AAD Client**), log in with a user that is a member of your cluster admin group in the Azure AD tenet you selected to be used for Kubernetes Cluster API RBAC. Also this is where any specified Azure AD conditional access policies would take effect if they had been applied. Remember, the identity you log in here with is the identity you're performing cluster management commands (e.g. `kubectl`) as.

   ```bash
   kubectl get nodes
   ```

   If all is successful you should see something like:

   ```output
   NAME                                  STATUS   ROLES   AGE   VERSION
   aks-npinscope01-26621167-vmss000000   Ready    agent   20m   v1.20.2
   aks-npinscope01-26621167-vmss000001   Ready    agent   20m   v1.20.2
   aks-npooscope01-26621167-vmss000000   Ready    agent   20m   v1.20.2
   aks-npooscope01-26621167-vmss000001   Ready    agent   20m   v1.20.2
   aks-npsystem-26621167-vmss000000      Ready    agent   20m   v1.20.2
   aks-npsystem-26621167-vmss000001      Ready    agent   20m   v1.20.2
   aks-npsystem-26621167-vmss000002      Ready    agent   20m   v1.20.2
   ```

1. _From your Azure Bastion connection_, bootstrap Flux.

   ```bash
   git clone https://github.com/[[YOUR_GITHUB_ACCOUNT_NAME]]/aks-regulated-cluster
   cd aks-regulated-cluster/cluster-manifests

   # Apply the Flux CRDs before applying the rest of flux (to avoid a race condition in the sync settings)
   kubectl apply -f flux-system/gotk-crds.yaml
   kubectl apply -k flux-system
   ```

   > The Flux CLI does have a built-in bootstrap feature. To ensure everyone using this walkthrough has a consistent experience (not one based on what version of Flux cli they may have installed), we've performed that bootstrap process more "manually" above. Also, you might consider doing the same with your production clusters; as all manifests you apply should be well-known, intentional, and auditable. Doing so will eliminate any guess work from what is or is not being deployed. It does mean that you'll manually be managing some manifests that could otherwise be managed in a more opaque way, and we suggest getting comfortable with that notion. You'll see this play out not just in Flux, but in Open Service Mesh, Falco, etc. Ensure you understand the risks associated with (and benefits gained from) whatever _convenance_ solutions you bring to your cluster (helm, cli "installer" commands, etc.) and you're comfortable with that layer of indirection bring introduced.

   Validate that flux has been bootstrapped.

   ```bash
   kubectl wait --namespace flux-system --for=condition=available deployment/source-controller --timeout=90s

   # If you have flux cli installed you can also inspect using the following commands
   # (The default jump box image created with this walkthrough has the flux cli installed.)
   flux check --components source-controller,kustomize-controller
   flux get sources git
   flux get kustomizations
   ```

1. Disconnect from the jump box and Azure Bastion.

Generally speaking, this will be the last time you should need to use direct cluster access tools like `kubectl` for day-to-day configuration operations on this cluster (outside of live-site situations). Between ARM for Azure resource definitions and the application of Kubernetes manifests via Flux, all normal configuration activities can be performed without the need to use `kubectl`. You will however use it for the upcoming workload deployment. This is because the SDLC component of workloads are not in scope for this reference implementation, as this is focused the infrastructure and baseline configuration.

**Typically all of the above bootstrapping steps would be codified in a release pipeline so that there would be NO NEED to perform any steps manually.** We're performing the steps manually here, like we have with all content so far, for illustrative purposes of the steps required. Once you have a safe deployment practice documented (both for internal team reference and for compliance needs), you can then put those actions into an auditable deployment pipeline, that combines deploying the infrastructure with the immediate follow up bootstrapping the cluster. Your workload(s) have a distinct lifecycle from your cluster and as such are managed via another pipeline. But bootstrapping your cluster should be seen as a direct and immediate continuation of the deployment of your cluster. There is a subnet allocated in this reference specifically for your build agents to perform unattended, "last mile" deployment, and configuration needs for your cluster. There is no compute deployed to that subnet, but typically this is where you'd put in a VM Scale Set as a [GitHub Action Self-Hosted Runner](https://docs.github.com/actions/hosting-your-own-runners/about-self-hosted-runners) or [Azure DevOps Self-Hosted Agent Pool](https://docs.microsoft.com/azure/devops/pipelines/agents/scale-set-agents?view=azure-devops). This compute should be a hardened, minimal installation set, and monitored. Just like a jump box, this compute will span two distinct security zones; in this case, unattended, externally managed GitHub Workflow definitions and your cluster.

## Live-site cluster access alternatives

If you wish to add an auditable layer of indirection between users and the cluster for live-site issues, you might consider a ChatOps approach, in which commands against the cluster are executed by dedicated, hardened compute in a subnet like the one above for deployment but are fronted by a Microsoft Teams integration. That gives you the ability to log _all commands_ executed against the cluster, without necessarily building an ops process based exclusively around jump boxes. Also, you may already have an IAM-gated IT automation platform in place in which pre-defined _actions_ can be constructed within. Its action runners would then execute within the ops subnet while the initial invocation of the actions is audited and controlled in the IT automation platform.

## GitOps configuration

The GitOps implementation in this reference architecture is intentionally simplistic. Flux is configured to simply monitor manifests in ALL namespaces. It doesn't account for concepts like:

* Built-in [bootstrapping support](https://toolkit.fluxcd.io/guides/installation/#bootstrap)
* [Multi-tenancy](https://github.com/fluxcd/flux2-multi-tenancy)
* [Private GitHub repos](https://toolkit.fluxcd.io/components/source/gitrepositories/#ssh-authentication)
* Kustomization [under/overlays](https://kubernetes.io/docs/tasks/manage-kubernetes-objects/kustomization/#bases-and-overlays)
* Flux's [Notifications controller](https://github.com/fluxcd/notification-controller) to [alert on changes](https://toolkit.fluxcd.io/guides/notifications/)
* Flux's [Helm controller](https://github.com/fluxcd/helm-controller) to [manage helm releases](https://toolkit.fluxcd.io/guides/helmreleases/)
* Flux's [monitoring](https://toolkit.fluxcd.io/guides/monitoring/) features

This reference implementation isn't going to dive into the nuances of git manifest organization. A lot of that depends on your namespacing, multi-tenant needs, multi-stage (dev, pre-prod, prod) deployment needs, multi-cluster needs, etc. **The key takeaway here is to ensure that you're managing your Kubernetes resources in a declarative manner with a reconcile loop, to achieve desired state configuration within your cluster.** Ensuring your cluster internally is managed by a single, appropriately-privileged, observable pipeline will aide in compliance. You'll have a git trail that aligns with a log trail from your GitOps toolkit.

## Public dependencies

As with any dependency your cluster or workload has, you'll want to minimize or eliminate your reliance on services in which you do not have an SLO or do not meet your observability/compliance requirements. Your cluster's GitOps operator(s) should **rely on a git repository that satisfies your reliability & compliance requirements**. Consider using a git mirroring approach to bring your cluster dependencies to be "network local" and provide a fault-tolerant syncing mechanism from centralized working repositories (like your organization's GitHub Enterprise private repositories). Following an approach like this will air gap git repositories as an external dependency, at the cost of added complexity.

## Security tooling

While Azure Kubernetes Service, Azure Defender for _topic_, and Azure Policy offers a secure platform foundation; the inner workings of your cluster are more of a relationship with you and Kubernetes than you and Azure. To that end, most customers bring their own security solutions that solve for their specific compliance and organizational requirements within their clusters. They often bring in holistic ISV solutions like [Aqua Security](https://www.aquasec.com/solutions/azure-container-security/), [Prisma Cloud Compute](hhttps://docs.paloaltonetworks.com/prisma/prisma-cloud/prisma-cloud-admin-compute/install/install_kubernetes.html), [StackRox](https://www.stackrox.com/solutions/microsoft-azure-security/), [Sysdig](https://sysdig.com/partners/microsoft-azure/), and/or [Tigera Enterprise](https://www.tigera.io/tigera-products/calico-enterprise/) to name a few. These solutions offer a suite of added security and reporting controls to your platform, but also come with their own licensing and support agreements.

Common features offered in ISV solutions like these:

* File Integrity Monitoring (FIM)
* Container-aware Anti-Virus solutions
* CVE Detection against admission requests and already executing images
* Advanced network segmentation
* Dangerous runtime container activity detection
* Workload level CIS benchmark reporting
* Managed network isolation and enhanced observability features (such as network flow visualizers)

Your dependency on or choice of in-cluster tooling to achieve your compliance needs cannot be suggested as a "one-size fits all" in this reference implementation. However, as a reminder of the need to solve for these, the Flux bootstrapping above deployed a _placeholder_ FIM and AV solution. **They are not functioning as a real FIM or AV**, simply a visual reminder that you will need to bring a suitable solution for compliance concerns.

This reference implementation also installs a very simplistic deployment of [Falco](https://falco.org/). It is not configured for alerts, nor tuned to any specific needs. It uses the default rules as they were defined when its manifests were generated. This is also being installed for illustrative purposes, and you're encouraged to evaluate if a solution like Falco is relevant to you. If so, in your final implementation, review and tune its deployment to fit your needs (E.g. add custom rules like [CVE detection](https://artifacthub.io/packages/search?ts_query_web=cve&org=falco), [sudo usage](https://artifacthub.io/packages/falco/security-hub/admin-activities), [basic FIM](https://artifacthub.io/packages/falco/security-hub/file-integrity-monitoring), [SSH Connection monitoring](https://artifacthub.io/packages/falco/security-hub/ssh-connections), and [Traefik containment](https://artifacthub.io/packages/falco/security-hub/traefik)). This tooling, as most security tooling will be, is highly-privileged within your cluster. Usually running as DaemonSets with access to the underlying node in a manor that is well beyond any typical workload in your cluster. Remember to consider the runtime compute requirements of your security tooling when sizing your cluster, as these can often be overlooked when initial cluster sizing conversations are happening.

It's worth repeating again, **most regulated customers are bringing ISV or open source security solutions to their clusters**. Azure Kubernetes Service is a managed Kubernetes platform, it does not imply that you will exclusively be using Microsoft products/solutions to solve your requirements. For the most part, after the deployment of the infrastructure and some out-of-the-box addons (like Azure Policy, Azure Monitor, AAD Pod Identity), you're in charge of what you choose to run in your hosted Kubernetes platform. Bring the business and compliance solving solutions you need to the cluster from the vast and ever-growing Kubernetes ecosystem.

**You should ensure all necessary tooling and related reporting/alerting is applied as part of your _initial bootstrapping process_ to ensure coverage _immediately_ after cluster creation.**

### Next step

:arrow_forward: [Prepare for the workload by installing its prerequisites](./12-workload-prerequisites.md)
