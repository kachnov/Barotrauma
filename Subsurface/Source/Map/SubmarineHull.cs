﻿using FarseerPhysics;
using FarseerPhysics.Collision;
using FarseerPhysics.Common;
using FarseerPhysics.Common.Decomposition;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Voronoi2;

namespace Subsurface
{
    class SubmarineBody
    {

        public List<Vector2> HullVertices
        {
            get;
            private set;
        }

        private Submarine sub;
        
        private Body body;

        private Vector2 speed;

        private Vector2 targetPosition;

        public Rectangle Borders
        {
            get;
            private set;
        }
                
        public Vector2 Speed
        {
            get { return speed; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                speed = value; 
            }
        }

        public Vector2 TargetPosition
        {
            get { return targetPosition; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                targetPosition = value;
            }
        }


        public Vector2 Center
        {
            get { return new Vector2(Borders.X + Borders.Width / 2, Borders.Y - Borders.Height / 2); }
        }

        public SubmarineBody(Submarine sub)
        {
            this.sub = sub;


            List<Vector2> convexHull = GenerateConvexHull();
            HullVertices = convexHull;

            for (int i = 0; i < convexHull.Count; i++)
            {
                convexHull[i] = ConvertUnits.ToSimUnits(convexHull[i]);
            }

            convexHull.Reverse();

            //get farseer 'vertices' from vectors
            Vertices shapevertices = new Vertices(convexHull);

            AABB hullAABB = shapevertices.GetAABB();

            Borders = new Rectangle(
                (int)ConvertUnits.ToDisplayUnits(hullAABB.LowerBound.X),
                (int)ConvertUnits.ToDisplayUnits(hullAABB.UpperBound.Y),
                (int)ConvertUnits.ToDisplayUnits(hullAABB.Extents.X * 2.0f),
                (int)ConvertUnits.ToDisplayUnits(hullAABB.Extents.Y * 2.0f));


            var triangulatedVertices = Triangulate.ConvexPartition(shapevertices, TriangulationAlgorithm.Bayazit);

            body = BodyFactory.CreateCompoundPolygon(GameMain.World, triangulatedVertices, 5.0f);
            body.BodyType = BodyType.Dynamic;

            body.CollisionCategories = Physics.CollisionMisc;
            body.CollidesWith = Physics.CollisionLevel;
            body.Restitution = 0.0f;
            body.FixedRotation = true;
            body.Awake = true;
            body.SleepingAllowed = false;
            body.IgnoreGravity = true;
            body.OnCollision += OnCollision;
            //body.OnSeparation += OnSeparation;
            body.UserData = this;
        }


        private List<Vector2> GenerateConvexHull()
        {
            List<Vector2> points = new List<Vector2>();

            Vector2 leftMost = Vector2.Zero;

            foreach (Structure wall in Structure.wallList)
            {
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        Vector2 corner = new Vector2(wall.Rect.X + wall.Rect.Width / 2.0f, wall.Rect.Y - wall.Rect.Height / 2.0f);
                        corner.X += x * wall.Rect.Width / 2.0f;
                        corner.Y += y * wall.Rect.Height / 2.0f;

                        if (points.Contains(corner)) continue;

                        points.Add(corner);
                        if (leftMost == Vector2.Zero || corner.X < leftMost.X) leftMost = corner;
                    }
                }
            }

            List<Vector2> hullPoints = new List<Vector2>();

            Vector2 currPoint = leftMost;
            Vector2 endPoint;
            do
            {
                hullPoints.Add(currPoint);
                endPoint = points[0];

                for (int i = 1; i < points.Count; i++)
                {
                    if ((currPoint == endPoint)
                        || (MathUtils.VectorOrientation(currPoint, endPoint, points[i]) == -1))
                    {
                        endPoint = points[i];
                    }
                }

                currPoint = endPoint;

            }
            while (endPoint != hullPoints[0]);

            return hullPoints;
        }


        float collisionRigidness = 1.0f;

        public void Update(float deltaTime)
        {
            Vector2 translateAmount = speed * deltaTime;
            translateAmount += ConvertUnits.ToDisplayUnits(body.Position) * collisionRigidness;

            if (targetPosition != Vector2.Zero && Vector2.Distance(targetPosition, sub.Position) > 50.0f)
            {
                translateAmount += (targetPosition - sub.Position) * 0.01f;
            }
            else
            {
                targetPosition = Vector2.Zero;
            }

            sub.Translate(translateAmount);

            //-------------------------

            Vector2 totalForce = CalculateBuoyancy();

            float dragCoefficient = 0.00001f;

            float speedLength = (speed == Vector2.Zero) ? 0.0f : speed.Length();
            float drag = speedLength * speedLength * dragCoefficient * mass;

            if (speed != Vector2.Zero)
            {
                totalForce += -Vector2.Normalize(speed) * drag;
            }

            ApplyForce(totalForce);

            //hullBodies[0].body.LinearVelocity = -hullBodies[0].body.Position;

            //hullBody.SetTransform(Vector2.Zero , 0.0f);
            body.SetTransform(Vector2.Zero, 0.0f);// .LinearVelocity = -body.Position / (float)Physics.step;
            body.LinearVelocity = Vector2.Zero;

        }

        private Vector2 CalculateBuoyancy()
        {
            float waterVolume = 0.0f;
            float volume = 0.0f;
            foreach (Hull hull in Hull.hullList)
            {
                waterVolume += hull.Volume;
                volume += hull.FullVolume;
            }

            float waterPercentage = waterVolume / volume;

            float neutralPercentage = 0.07f;

            float buoyancy = neutralPercentage - waterPercentage;
            buoyancy *= mass * 30.0f;

            return new Vector2(0.0f, buoyancy);
        }

        float mass = 10000.0f;
        public void ApplyForce(Vector2 force)
        {
            speed += force / mass;
        }

        public bool OnCollision(Fixture f1, Fixture f2, Contact contact)
        {
            VoronoiCell cell = f2.Body.UserData as VoronoiCell;
            if (cell == null)
            {
                speed = new Vector2(speed.X * 0.9f, speed.Y * 0.2f);
                return true;
            }

            Vector2 normal = contact.Manifold.LocalNormal;
            Vector2 simSpeed = ConvertUnits.ToSimUnits(speed) + body.LinearVelocity;
            float impact = Vector2.Dot(simSpeed, normal);

            Vector2 u = Vector2.Dot(simSpeed, -normal) * -normal;
            Vector2 w = simSpeed - u;

            Vector2 limbForce = normal * impact;

            float length = limbForce.Length();
            if (length > 10.0f) limbForce = (limbForce / length) * 10.0f;

            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.CurrentHull == null) continue;

                if (impact > 2.0f) c.AnimController.StunTimer = (impact - 2.0f) * 0.1f;

                foreach (Limb limb in c.AnimController.Limbs)
                {
                    if (c.AnimController.LowestLimb == limb) continue;
                    limb.body.ApplyLinearImpulse(limb.Mass * limbForce);
                }
            }

            if (impact >= 1.0f)
            {
                AmbientSoundManager.PlayDamageSound(DamageSoundType.StructureBlunt, impact * 10.0f, cell.body);

                FarseerPhysics.Common.FixedArray2<Vector2> worldPoints;
                contact.GetWorldManifold(out normal, out worldPoints);

                AmbientSoundManager.PlayDamageSound(DamageSoundType.StructureBlunt, impact * 10.0f, ConvertUnits.ToDisplayUnits(worldPoints[0]));

                GameMain.GameScreen.Cam.Shake = impact * 2.0f;
            }

            System.Diagnostics.Debug.WriteLine("IMPACT: " + impact + " normal: " + normal + " simspeed: " + simSpeed + " u: " + u + " w: " + w);
            if (impact < 3.0f)
            {
                speed = ConvertUnits.ToDisplayUnits(w * 0.45f - u * 0.25f);
                return true;
            }
            else
            {
                speed = ConvertUnits.ToDisplayUnits(w * 0.9f + u * 0.5f);

                //FixedArray2<Vector2> worldPoints;
                //contact.GetWorldManifold(out normal, out worldPoints);

                //if (contact.Manifold.PointCount >= 1)
                //{
                //    Vector2 contactPoint = worldPoints[0];

                //    Body wallBody = Submarine.PickBody(contactPoint, contactPoint + normal, new List<Body>() { cell.body });

                //    if (wallBody!=null && wallBody.UserData!=null)
                //    {
                //        Structure s = wallBody.UserData as Structure;
                //    }
                //}

                foreach (GraphEdge ge in cell.edges)
                {
                    Body wallBody = Submarine.PickBody(
                        ConvertUnits.ToSimUnits(ge.point1 + GameMain.GameSession.Level.Position + normal),
                        ConvertUnits.ToSimUnits(ge.point2 + GameMain.GameSession.Level.Position + normal), new List<Body>() { cell.body });
                    if (wallBody == null || wallBody.UserData == null) continue;

                    Structure structure = wallBody.UserData as Structure;
                    if (structure == null) continue;
                    structure.AddDamage(
                        structure.FindSectionIndex(ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition)), impact*50.0f);
                }
            }


            collisionRigidness = 0.8f;

            return true;
        }

        //public void OnSeparation(Fixture f1, Fixture f2)
        //{
        //    collidingCell = null;
        //}
    }
}