﻿using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;
using Barotrauma.Networking;
using Lidgren.Network;
#if CLIENT
using Barotrauma.Lights;
#endif

namespace Barotrauma.Items.Components
{
    partial class LightComponent : Powered, IServerSerializable, IDrawableComponent
    {
        private Color lightColor;

        private float range;

        private float lightBrightness;

        private float flicker;

        private bool castShadows;

        [Editable, HasDefaultValue(100.0f, true)]
        public float Range
        {
            get { return range; }
            set
            {
                range = MathHelper.Clamp(value, 0.0f, 2048.0f);
            }
        }

        [Editable, HasDefaultValue(true, true)]
        public bool CastShadows
        {
            get { return castShadows; }
            set
            {
                castShadows = value;
#if CLIENT
                if (light != null) light.CastShadows = value;
#endif
            }
        }

        [Editable, HasDefaultValue(false, true)]
        public bool IsOn
        {
            get { return IsActive; }
            set
            {
                if (IsActive == value) return;
                
                IsActive = value;
                if (GameMain.Server != null) item.CreateServerEvent(this);
            }
        }
        
        [HasDefaultValue(0.0f, false)]
        public float Flicker
        {
            get { return flicker; }
            set
            {
                flicker = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }

        [InGameEditable, HasDefaultValue("1.0,1.0,1.0,1.0", true)]
        public string LightColor
        {
            get { return XMLExtensions.Vector4ToString(lightColor.ToVector4(), "0.00"); }
            set
            {
                Vector4 newColor = XMLExtensions.ParseToVector4(value, false);
                newColor.X = MathHelper.Clamp(newColor.X, 0.0f, 1.0f);
                newColor.Y = MathHelper.Clamp(newColor.Y, 0.0f, 1.0f);
                newColor.Z = MathHelper.Clamp(newColor.Z, 0.0f, 1.0f);
                newColor.W = MathHelper.Clamp(newColor.W, 0.0f, 1.0f);
                lightColor = new Color(newColor);
            }
        }

        public override void Move(Vector2 amount)
        {
#if CLIENT
            light.Position += amount;
#endif
        }

        public override bool IsActive
        {
            get
            {
                return base.IsActive;
            }

            set
            {
                base.IsActive = value;
#if CLIENT
                if (light == null) return;
                light.Color = value ? lightColor : Color.Transparent;
                if (!value) lightBrightness = 0.0f;
#endif
            }
        }

        public LightComponent(Item item, XElement element)
            : base (item, element)
        {
#if CLIENT
            light = new LightSource(element);
            light.ParentSub = item.CurrentHull == null ? null : item.CurrentHull.Submarine;
            light.Position = item.Position;
            light.CastShadows = castShadows;
#endif

            IsActive = IsOn;

            //foreach (XElement subElement in element.Elements())
            //{
            //    if (subElement.Name.ToString().ToLowerInvariant() != "sprite") continue;

            //    light.LightSprite = new Sprite(subElement);
            //    break;
            //}
        }
        
        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

#if CLIENT
            light.ParentSub = item.Submarine;
#endif
            
#if CLIENT
            if (item.Container != null)
            {
                light.Color = Color.Transparent;
                return;
            }
#endif

            if (item.body != null)
            {
#if CLIENT
                light.Position = item.Position;
                light.Rotation = item.body.Dir > 0.0f ? item.body.Rotation : item.body.Rotation - MathHelper.Pi;
#endif
                if (!item.body.Enabled)
                {
#if CLIENT
                    light.Color = Color.Transparent;
#endif
                    return;
                }
            }
            
            if (powerConsumption == 0.0f)
            {
                voltage = 1.0f;
            }
            else
            {
                currPowerConsumption = powerConsumption;                
            }

            if (Rand.Range(0.0f, 1.0f) < 0.05f && voltage < Rand.Range(0.0f, minVoltage))
            {
#if CLIENT
                if (voltage > 0.1f) sparkSounds[Rand.Int(sparkSounds.Length)].Play(1.0f, 400.0f, item.WorldPosition);
#endif
                lightBrightness = 0.0f;
            }
            else
            {
                lightBrightness = MathHelper.Lerp(lightBrightness, Math.Min(voltage, 1.0f), 0.1f);
            }

#if CLIENT
            light.Color = lightColor * lightBrightness * (1.0f-Rand.Range(0.0f,Flicker));
            light.Range = range * (float)Math.Sqrt(lightBrightness);
#endif

            voltage = 0.0f;
        }
        
        public override bool Use(float deltaTime, Character character = null)
        {
            return true;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();

#if CLIENT
            light.Remove();
#endif
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power=0.0f)
        {
            base.ReceiveSignal(stepsTaken, signal, connection, source, sender, power);

            switch (connection.Name)
            {
                case "toggle":
                    IsActive = !IsActive;
                    break;
                case "set_state":           
                    IsActive = (signal != "0");                   
                    break;
                case "set_color":
                    LightColor = signal;
                    break;
            }
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(IsOn);
        }
    }
}
