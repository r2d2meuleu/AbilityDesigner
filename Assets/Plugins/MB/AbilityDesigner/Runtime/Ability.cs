﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;

namespace MB.AbilityDesigner
{
    public enum Result { Success, Fail, Running }

    [CreateAssetMenu(menuName = "Ability Designer/Ability", order = 52)]
    public class Ability : ScriptableObject
    {
        [SerializeField]
        private string m_Title;
        public string title { get { return m_Title; } internal set { m_Title = value; } }
        [SerializeField]
        private string m_Description;
        public string description { get { return m_Description; } internal set { m_Description = value; } }
        [SerializeField]
        private Texture m_Icon;
        public Texture icon { get { return m_Icon; } internal set { m_Icon = value; } }

        [SerializeField]
        private bool m_DefaultLock = true;
        public bool defaultLock { get { return m_DefaultLock; } internal set { m_DefaultLock = value; } }

        [System.NonSerialized]
        private bool m_Unlocked;
        public bool unlocked { get { return m_Unlocked; } set { m_Unlocked = value; } }

        [SerializeField]
        private CastRule[] m_CastRules = new CastRule[0];
        public CastRule[] castRules { get { return m_CastRules; } internal set { m_CastRules = value; } }

        [SerializeField]
        private int m_PoolingChunkSize;
        public int poolingChunkSize { get { return m_PoolingChunkSize; } internal set { m_PoolingChunkSize = value; } }

        [SerializeField]
        private PhaseList[] m_PhaseLists = new PhaseList[0];
        internal PhaseList[] phaseLists { get { return m_PhaseLists; } set { m_PhaseLists = value; } }
        [SerializeField]
        private SubInstanceLink[] m_SubInstanceLinks = new SubInstanceLink[0];
        internal SubInstanceLink[] subInstanceLinks { get { return m_SubInstanceLinks; } set { m_SubInstanceLinks = value; } }
        [SerializeField]
        private SharedVariable[] m_SharedVariables = new SharedVariable[0];
        internal SharedVariable[] sharedVariables { get { return m_SharedVariables; } set { m_SharedVariables = value; } }

        internal AbilityInstanceManager instanceManager { get; set; }

        public void Cast(IAbilityUser originator, params IAbilityUser[] targets)
        {
            if (instanceManager == null || !unlocked)
            {
                return;
            }

            string id = originator.GetInstanceID() + "";

            if (targets.Length <= 0)
            {
                if (!instanceManager.IsCastLegitimate(id))
                {
                    return;
                }
                instanceManager.RequestInstance(id).Cast(originator, null);
            }

            for (int t = 0; t < targets.Length; t++)
            {
                if (!instanceManager.IsCastLegitimate(id))
                {
                    return;
                }
                instanceManager.RequestInstance(id).Cast(originator, targets[t]);
            }
        }

        internal void Return(string id, AbilityInstance instance)
        {
            if (instanceManager == null)
            {
                return;
            }

            instanceManager.ReturnInstance(id, instance);
        }

        public float GetCooldownProgress(string id)
        {
            for (int c = 0; c < castRules.Length; c++)
            {
                AmunationCooldown amunationCooldown = castRules[c] as AmunationCooldown;
                if (amunationCooldown != null)
                {
                    return amunationCooldown.GetProgress(instanceManager.GetCooldown(id));
                }
                SimpleCooldown simpleCooldown = castRules[c] as SimpleCooldown;
                if (simpleCooldown != null)
                {
                    return simpleCooldown.GetProgress(instanceManager.GetCooldown(id));
                }
            }
            return 1f;
        }

        public GameObject CreateRuntimeInstance()
        {
            GameObject gameObject = new GameObject(title + " Runtime Instance");
            AbilityInstance instance = gameObject.AddComponent<AbilityInstance>();

            m_Unlocked = m_DefaultLock;

            // Clone all phases to the ability instance for runtime use
            instance.phaseLists = new PhaseList[phaseLists.Length];
            for (int p = 0; p < phaseLists.Length; p++)
            {
                instance.phaseLists[p] = phaseLists[p].Clone();
            }

            // Copy all sub instance links to the ability instance
            instance.subInstanceLinks = new SubInstanceLink[subInstanceLinks.Length];
            for (int s = 0; s < subInstanceLinks.Length; s++)
            {
                SubInstanceLink newLink = Instantiate(subInstanceLinks[s]);

                // Create subinstance obj
                GameObject linkObj;
                if (subInstanceLinks[s].obj == null)
                {
                    // Create an empty game object
                    linkObj = new GameObject(title + " (Sub Instance: " + subInstanceLinks[s].title + ")");
                }
                else
                {
                    // Instantiate the prefab given
                    linkObj = Instantiate(subInstanceLinks[s].obj);
                    linkObj.name = title + " (Sub Instance: " + subInstanceLinks[s].title + ")";
                }
                
                // Set the parent to the ability instance
                linkObj.transform.SetParent(gameObject.transform);
                linkObj.transform.localPosition = Vector3.zero;

                // Give the generated object to the sub instance link
                newLink.obj = linkObj;
                newLink.CacheComponents();

                // Push the sub instance link into the list of the instance
                instance.subInstanceLinks[s] = newLink;
            }

            // Clone all shared variables for the ability instance
            instance.sharedVariables = new SharedVariable[sharedVariables.Length];
            for (int s = 0; s < sharedVariables.Length; s++)
            {
                instance.sharedVariables[s] = Instantiate(sharedVariables[s]);
            }

            List<SubInstanceLink> subInstanceLinksList = new List<SubInstanceLink>(subInstanceLinks);
            List<SharedVariable> sharedVariablesList = new List<SharedVariable>(sharedVariables);

            for (int l = 0; l < phaseLists.Length; l++)
            {
                for (int p = 0; p < phaseLists[l].phases.Length; p++)
                {
                    // Redirecting sub instance links
                    SubInstanceLink[] addingSubInstanceLinks = new SubInstanceLink[phaseLists[l].phases[p].runForSubInstances.Length];
                    for (int s = 0; s < addingSubInstanceLinks.Length; s++)
                    {
                        SubInstanceLink link = phaseLists[l].phases[p].runForSubInstances[s];
                        addingSubInstanceLinks[s] = instance.subInstanceLinks[subInstanceLinksList.IndexOf(link)];
                    }
                    instance.phaseLists[l].phases[p].runForSubInstances = addingSubInstanceLinks;

                    // Redirecting shared variables
                    System.Type phaseType = phaseLists[l].phases[p].GetType();
                    FieldInfo[] allFields = phaseType.GetFields();

                    for (int f = 0; f < allFields.Length; f++)
                    {
                        if (allFields[f].FieldType.IsSubclassOf(typeof(SharedVariable)))
                        {
                            object value = allFields[f].GetValue(phaseLists[l].phases[p]);
                            SharedVariable sharedVariable = value as SharedVariable;
                            if (sharedVariable != null)
                            {
                                int index = sharedVariablesList.IndexOf(sharedVariable);
                                allFields[f].SetValue(instance.phaseLists[l].phases[p], instance.sharedVariables[index]);
                            }
                        }
                        
                        if (allFields[f].FieldType.IsSubclassOf(typeof(SubVariable<>)))
                        {
                            object value = allFields[f].GetValue(phaseLists[l].phases[p]);

                            MethodInfo cloneMethod = value.GetType().GetMethod("Clone", BindingFlags.Instance | BindingFlags.NonPublic);
                            object clone = cloneMethod.Invoke(value, new object[] { });


                            allFields[f].SetValue(instance.phaseLists[l].phases[p], clone);
                        }
                    }
                }
            }

            instance.Initiate();

            return gameObject;
        }
    }
}