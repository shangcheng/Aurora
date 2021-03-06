/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

/*
 * Revised August 26 2009 by Kitto Flora. ODEDynamics.cs replaces
 * ODEVehicleSettings.cs. It and ODEPrim.cs are re-organised:
 * ODEPrim.cs contains methods dealing with Prim editing, Prim
 * characteristics and Kinetic motion.
 * ODEDynamics.cs contains methods dealing with Prim Physical motion
 * (dynamics) and the associated settings. Old Linear and angular
 * motors for dynamic motion have been replace with  MoveLinear()
 * and MoveAngular(); 'Physical' is used only to switch ODE dynamic 
 * simualtion on/off; VEHICAL_TYPE_NONE/VEHICAL_TYPE_<other> is to
 * switch between 'VEHICLE' parameter use and general dynamics
 * settings use.
 */
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using log4net;
using OpenMetaverse;
using Ode.NET;
using OpenSim.Framework;
using OpenSim.Region.Physics.Manager;

namespace Aurora.Physics.AuroraOpenDynamicsEngine
{
    /// <summary>
    /// Various properties that ODE uses for AMotors but isn't exposed in ODE.NET so we must define them ourselves.
    /// </summary>

    public class AuroraODEPrim : PhysicsActor
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Vector3 _position;
        private Vector3 _velocity;
        private Vector3 _torque;
        private Vector3 m_lastVelocity;
        private Vector3 m_lastposition;
        private Quaternion m_lastorientation = new Quaternion();
        private Vector3 m_rotationalVelocity;
        private Vector3 _size;
        private Vector3 _acceleration;
        private Quaternion _orientation;
        private Vector3 m_taintposition;
        private Vector3 m_taintsize;
        private Vector3 m_taintVelocity;
        private Vector3 m_taintTorque;
        private Quaternion m_taintrot;
        public Vector3 m_angularlock = Vector3.One;
        private Vector3 m_taintAngularLock = Vector3.One;
        private bool m_needsTaintAngularLock = false;
        public IntPtr Amotor = IntPtr.Zero;
        public d.Matrix3 AmotorRotation = new d.Matrix3();

        private Vector3 m_PIDTarget;
        private float m_PIDTau;
        private float PID_D = 35f;
        private float PID_G = 25f;
        private bool m_usePID;

        // KF: These next 7 params apply to llSetHoverHeight(float height, integer water, float tau),
        // and are for non-VEHICLES only.

        private float m_PIDHoverHeight;
        private float m_PIDHoverTau;
        private bool m_useHoverPID;
        private PIDHoverType m_PIDHoverType = PIDHoverType.Ground;
        private float m_targetHoverHeight;
        private float m_groundHeight;
        private float m_waterHeight;
        private float m_buoyancy;                //KF: m_buoyancy should be set by llSetBuoyancy() for non-vehicle. 

        // private float m_tensor = 5f;
        private int body_autodisable_frames = 20;


        private const CollisionCategories m_default_collisionFlags = (CollisionCategories.Geom
                                                        | CollisionCategories.Space
                                                        | CollisionCategories.Body
                                                        | CollisionCategories.Character
                                                        );
        private bool m_taintshape;
        private bool m_taintPhysics;
        private bool m_collidesLand = true;
        private bool m_collidesWater;
        public bool m_returnCollisions;
        private bool testRealGravity = false;

        // Default we're a Geometry
        private CollisionCategories m_collisionCategories = (CollisionCategories.Geom);

        // Default, Collide with Other Geometries, spaces and Bodies
        private CollisionCategories m_collisionFlags = m_default_collisionFlags;

        public bool m_taintremove;
        public bool m_taintdisable;
        public bool m_disabled;
        public bool m_taintadd;
        public bool m_taintselected;
        public bool m_taintCollidesWater;
        //This disables the prim so that it cannot do much anything at all
        public bool m_frozen = false;

        public uint m_localID;

        //public GCHandle gc;
        private CollisionLocker ode;

        private bool m_taintforce = false;
        private bool m_taintaddangularforce = false;
        private Vector3 m_force;
        private List<Vector3> m_forcelist = new List<Vector3>();
        private List<Vector3> m_angularforcelist = new List<Vector3>();

        private IMesh _mesh;
        private PrimitiveBaseShape _pbs;
        private AuroraODEPhysicsScene _parent_scene;
        public IntPtr m_targetSpace = IntPtr.Zero;
        public IntPtr prim_geom;
        public IntPtr prev_geom;
        public IntPtr _triMeshData;

        private IntPtr _linkJointGroup = IntPtr.Zero;
        private PhysicsActor _parent;
        private PhysicsActor m_taintparent;

        private List<AuroraODEPrim> childrenPrim = new List<AuroraODEPrim>();

        private bool iscolliding;
        private bool m_isphysical;
        private bool m_isSelected;

        internal bool m_isVolumeDetect; // If true, this prim only detects collisions but doesn't collide actively

        private bool m_throttleUpdates;
        private int throttleCounter;
        public int m_interpenetrationcount;
        public float m_collisionscore;
        private int m_crossingfailures;

        public bool outofBounds;
        private float m_density = 10.000006836f; // Aluminum g/cm3;

        public bool _zeroFlag;
        private int m_lastUpdateSent = 0;

        public IntPtr Body = IntPtr.Zero;
        public String m_primName;
        private Vector3 _target_velocity;
        public d.Mass pMass;

        public int m_eventsubscription;
        private CollisionEventUpdate CollisionEventsThisFrame;

        private IntPtr m_linkJoint = IntPtr.Zero;

        public volatile bool childPrim;

        private AuroraODEDynamics m_vehicle;

        internal int m_material = (int)Material.Wood;

        public AuroraODEPrim(String primName, AuroraODEPhysicsScene parent_scene, Vector3 pos, Vector3 size,
                       Quaternion rotation, IMesh mesh, PrimitiveBaseShape pbs, bool pisPhysical, CollisionLocker dode)
        {
            m_vehicle = new AuroraODEDynamics();
            //gc = GCHandle.Alloc(prim_geom, GCHandleType.Pinned);
            ode = dode;
            if (!pos.IsFinite())
            {
                pos = new Vector3(((float)Constants.RegionSize * 0.5f), ((float)Constants.RegionSize * 0.5f),
                    parent_scene.GetTerrainHeightAtXY(((float)Constants.RegionSize * 0.5f), ((float)Constants.RegionSize * 0.5f)) + 0.5f);
                m_log.Warn("[PHYSICS]: Got nonFinite Object create Position");
            }
            _position = pos;
            m_taintposition = pos;
            PID_D = parent_scene.bodyPIDD;
            PID_G = parent_scene.bodyPIDG;
            m_density = parent_scene.geomDefaultDensity;
            // m_tensor = parent_scene.bodyMotorJointMaxforceTensor;
            body_autodisable_frames = parent_scene.bodyFramesAutoDisable;


            prim_geom = IntPtr.Zero;
            prev_geom = IntPtr.Zero;

            if (!pos.IsFinite())
            {
                size = new Vector3(0.5f, 0.5f, 0.5f);
                m_log.Warn("[PHYSICS]: Got nonFinite Object create Size");
            }

            if (size.X <= 0) size.X = 0.01f;
            if (size.Y <= 0) size.Y = 0.01f;
            if (size.Z <= 0) size.Z = 0.01f;

            _size = size;
            m_taintsize = _size;

            if (!QuaternionIsFinite(rotation))
            {
                rotation = Quaternion.Identity;
                m_log.Warn("[PHYSICS]: Got nonFinite Object create Rotation");
            }

            _orientation = rotation;
            m_taintrot = _orientation;
            _mesh = mesh;
            _pbs = pbs;

            _parent_scene = parent_scene;
            m_targetSpace = (IntPtr)0;

            if (pos.Z < 0)
                m_isphysical = false;
            else
            {
                m_isphysical = pisPhysical;
                // If we're physical, we need to be in the master space for now.
                // linksets *should* be in a space together..  but are not currently
                if (m_isphysical)
                    m_targetSpace = _parent_scene.space;
            }
            m_primName = primName;
            m_taintadd = true;
            _parent_scene.AddPhysicsActorTaint(this);
            //  don't do .add() here; old geoms get recycled with the same hash
        }

        public override int PhysicsActorType
        {
            get { return (int) ActorTypes.Prim; }
            set { return; }
        }

        public override bool SetAlwaysRun
        {
            get { return false; }
            set { return; }
        }

        public override uint LocalID
        {
            set {
                //m_log.Info("[PHYSICS]: Setting TrackerID: " + value);
                m_localID = value; }
        }

        public override bool Grabbed
        {
            set { return; }
        }

        public override bool VolumeDetect
        {
            get { return m_isVolumeDetect; }
        }

        public override bool Selected
        {
            set {
        
            
                // This only makes the object not collidable if the object
                // is physical or the object is modified somehow *IN THE FUTURE*
                // without this, if an avatar selects prim, they can walk right
                // through it while it's selected
                m_collisionscore = 0;
                m_taintselected = value;
                if ((m_isphysical && !_zeroFlag) || !value)
                {
                    _parent_scene.AddPhysicsActorTaint(this);
                }
                else
                {
                    m_isSelected = value;
                }
                if (m_isSelected) disableBodySoft();
            }
        }

        public void SetGeom(IntPtr geom)
        {
            prev_geom = prim_geom;
            prim_geom = geom;
//Console.WriteLine("SetGeom to " + prim_geom + " for " + m_primName);
            if (prim_geom != IntPtr.Zero)
            {
                d.GeomSetCategoryBits(prim_geom, (int)m_collisionCategories);
                d.GeomSetCollideBits(prim_geom, (int)m_collisionFlags);
            }

            if (childPrim)
            {
                if (_parent != null && _parent is AuroraODEPrim)
                {
                    AuroraODEPrim parent = (AuroraODEPrim)_parent;
//Console.WriteLine("SetGeom calls ChildSetGeom");
                    parent.ChildSetGeom(this);
                }
            }
            //m_log.Warn("Setting Geom to: " + prim_geom);
        }

        

        public void enableBodySoft()
        {
            if (!childPrim)
            {
                if (m_isphysical && Body != IntPtr.Zero)
                {
                    d.BodyEnable(Body);
                    if (m_vehicle.Type != Vehicle.TYPE_NONE)
                        m_vehicle.Enable(Body, this,_parent_scene);
                }

                m_disabled = false;
            }
        }

        public void disableBodySoft()
        {
            m_disabled = true;

            m_vehicle.Disable(this);
            if (m_isphysical && Body != IntPtr.Zero)
            {
                d.BodyDisable(Body);
            }
        }

        public void enableBody()
        {
            // Don't enable this body if we're a child prim
            // this should be taken care of in the parent function not here
            if (!childPrim)
            {
                // Sets the geom to a body
                Body = d.BodyCreate(_parent_scene.world);

                setMass();
                d.BodySetPosition(Body, _position.X, _position.Y, _position.Z);
                d.Quaternion myrot = new d.Quaternion();
                myrot.X = _orientation.X;
                myrot.Y = _orientation.Y;
                myrot.Z = _orientation.Z;
                myrot.W = _orientation.W;
                d.BodySetQuaternion(Body, ref myrot);
                d.GeomSetBody(prim_geom, Body);
                m_collisionCategories |= CollisionCategories.Body;
                m_collisionFlags |= (CollisionCategories.Land | CollisionCategories.Wind);

                d.GeomSetCategoryBits(prim_geom, (int)m_collisionCategories);
                d.GeomSetCollideBits(prim_geom, (int)m_collisionFlags);

                d.BodySetAutoDisableFlag(Body, true);
                d.BodySetAutoDisableSteps(Body, body_autodisable_frames);
                d.BodySetDamping(Body, 1, 1);
                
                // disconnect from world gravity so we can apply buoyancy
                if(!testRealGravity)
                    d.BodySetGravityMode(Body, false);

                m_interpenetrationcount = 0;
                m_collisionscore = 0;
                m_disabled = false;

                // The body doesn't already have a finite rotation mode set here
                if ((!m_angularlock.ApproxEquals(Vector3.One, 0.0f)) && _parent == null)
                {
                    createAMotor(m_angularlock);
                }
                if (m_vehicle.Type != Vehicle.TYPE_NONE)
                {
                    m_vehicle.Enable(Body, this, _parent_scene);
                }

                _parent_scene.addActivePrim(this);
            }
        }

        #region Mass Calculation

        private float CalculateMass()
        {
            float volume = _size.X * _size.Y * _size.Z; // default
            float tmp;

            float returnMass = 0;
            float hollowAmount = (float)_pbs.ProfileHollow * 2.0e-5f;
            float hollowVolume = hollowAmount * hollowAmount; 
            
            switch (_pbs.ProfileShape)
            {
                case ProfileShape.Square:
                    // default box

                    if (_pbs.PathCurve == (byte)Extrusion.Straight)
                        {
                        if (hollowAmount > 0.0)
                            {
                            switch (_pbs.HollowShape)
                                {
                                case HollowShape.Square:
                                case HollowShape.Same:
                                    break;

                                case HollowShape.Circle:

                                    hollowVolume *= 0.78539816339f;
                                    break;

                                case HollowShape.Triangle:

                                    hollowVolume *= (0.5f * .5f);
                                    break;

                                default:
                                    hollowVolume = 0;
                                    break;
                                }
                            volume *= (1.0f - hollowVolume);
                            }
                        }

                    else if (_pbs.PathCurve == (byte)Extrusion.Curve1)
                        {
                        //a tube 

                        volume *= 0.78539816339e-2f * (float)(200 - _pbs.PathScaleX);
                        tmp= 1.0f -2.0e-2f * (float)(200 - _pbs.PathScaleY);
                        volume -= volume*tmp*tmp;
                        
                        if (hollowAmount > 0.0)
                            {
                            hollowVolume *= hollowAmount;
                            
                            switch (_pbs.HollowShape)
                                {
                                case HollowShape.Square:
                                case HollowShape.Same:
                                    break;

                                case HollowShape.Circle:
                                    hollowVolume *= 0.78539816339f;;
                                    break;

                                case HollowShape.Triangle:
                                    hollowVolume *= 0.5f * 0.5f;
                                    break;
                                default:
                                    hollowVolume = 0;
                                    break;
                                }
                            volume *= (1.0f - hollowVolume);
                            }
                        }

                    break;

                case ProfileShape.Circle:

                    if (_pbs.PathCurve == (byte)Extrusion.Straight)
                        {
                        volume *= 0.78539816339f; // elipse base

                        if (hollowAmount > 0.0)
                            {
                            switch (_pbs.HollowShape)
                                {
                                case HollowShape.Same:
                                case HollowShape.Circle:
                                    break;

                                case HollowShape.Square:
                                    hollowVolume *= 0.5f * 2.5984480504799f;
                                    break;

                                case HollowShape.Triangle:
                                    hollowVolume *= .5f * 1.27323954473516f;
                                    break;

                                default:
                                    hollowVolume = 0;
                                    break;
                                }
                            volume *= (1.0f - hollowVolume);
                            }
                        }

                    else if (_pbs.PathCurve == (byte)Extrusion.Curve1)
                        {
                        volume *= 0.61685027506808491367715568749226e-2f * (float)(200 - _pbs.PathScaleX);
                        tmp = 1.0f - .02f * (float)(200 - _pbs.PathScaleY);
                        volume *= (1.0f - tmp * tmp);
                        
                        if (hollowAmount > 0.0)
                            {

                            // calculate the hollow volume by it's shape compared to the prim shape
                            hollowVolume *= hollowAmount;

                            switch (_pbs.HollowShape)
                                {
                                case HollowShape.Same:
                                case HollowShape.Circle:
                                    break;

                                case HollowShape.Square:
                                    hollowVolume *= 0.5f * 2.5984480504799f;
                                    break;

                                case HollowShape.Triangle:
                                    hollowVolume *= .5f * 1.27323954473516f;
                                    break;

                                default:
                                    hollowVolume = 0;
                                    break;
                                }
                            volume *= (1.0f - hollowVolume);
                            }
                        }
                    break;

                case ProfileShape.HalfCircle:
                    if (_pbs.PathCurve == (byte)Extrusion.Curve1)
                    {
                    volume *= 0.52359877559829887307710723054658f;
                    }
                    break;

                case ProfileShape.EquilateralTriangle:

                    if (_pbs.PathCurve == (byte)Extrusion.Straight)
                        {
                        volume *= 0.32475953f;

                        if (hollowAmount > 0.0)
                            {

                            // calculate the hollow volume by it's shape compared to the prim shape
                            switch (_pbs.HollowShape)
                                {
                                case HollowShape.Same:
                                case HollowShape.Triangle:
                                    hollowVolume *= .25f;
                                    break;

                                case HollowShape.Square:
                                    hollowVolume *= 0.499849f * 3.07920140172638f;
                                    break;

                                case HollowShape.Circle:
                                    // Hollow shape is a perfect cyllinder in respect to the cube's scale
                                    // Cyllinder hollow volume calculation

                                    hollowVolume *= 0.1963495f * 3.07920140172638f;
                                    break;

                                default:
                                    hollowVolume = 0;
                                    break;
                                }
                            volume *= (1.0f - hollowVolume);
                            }
                        }
                    else if (_pbs.PathCurve == (byte)Extrusion.Curve1)
                        {
                        volume *= 0.32475953f;
                        volume *= 0.01f * (float)(200 - _pbs.PathScaleX);
                        tmp = 1.0f - .02f * (float)(200 - _pbs.PathScaleY);
                        volume *= (1.0f - tmp * tmp);

                        if (hollowAmount > 0.0)
                            {

                            hollowVolume *= hollowAmount;

                            switch (_pbs.HollowShape)
                                {
                                case HollowShape.Same:
                                case HollowShape.Triangle:
                                    hollowVolume *= .25f;
                                    break;

                                case HollowShape.Square:
                                    hollowVolume *= 0.499849f * 3.07920140172638f;
                                    break;

                                case HollowShape.Circle:

                                    hollowVolume *= 0.1963495f * 3.07920140172638f;
                                    break;

                                default:
                                    hollowVolume = 0;
                                    break;
                                }
                            volume *= (1.0f - hollowVolume);
                            }
                        }
                        break;                  

                default:
                    break;
                }



            float taperX1;
            float taperY1;
            float taperX;
            float taperY;
            float pathBegin;
            float pathEnd;
            float profileBegin;
            float profileEnd;

            if (_pbs.PathCurve == (byte)Extrusion.Straight || _pbs.PathCurve == (byte)Extrusion.Flexible)
                {
                taperX1 = _pbs.PathScaleX * 0.01f;
                if (taperX1 > 1.0f)
                    taperX1 = 2.0f - taperX1;
                taperX = 1.0f - taperX1;

                taperY1 = _pbs.PathScaleY * 0.01f;
                if (taperY1 > 1.0f)
                    taperY1 = 2.0f - taperY1;
                taperY = 1.0f - taperY1;
                }
            else
                {
                taperX = _pbs.PathTaperX * 0.01f;
                if (taperX < 0.0f)
                    taperX = -taperX;
                taperX1 = 1.0f - taperX;

                taperY = _pbs.PathTaperY * 0.01f;
                if (taperY < 0.0f)
                    taperY = -taperY;               
                taperY1 = 1.0f - taperY;

                }


            volume *= (taperX1 * taperY1 + 0.5f * (taperX1 * taperY + taperX * taperY1) + 0.3333333333f * taperX * taperY);

            pathBegin = (float)_pbs.PathBegin * 2.0e-5f;
            pathEnd = 1.0f - (float)_pbs.PathEnd * 2.0e-5f;
            volume *= (pathEnd - pathBegin);

// this is crude aproximation
            profileBegin = (float)_pbs.ProfileBegin * 2.0e-5f;
            profileEnd = 1.0f - (float)_pbs.ProfileEnd * 2.0e-5f;
            volume *= (profileEnd - profileBegin);

            returnMass = m_density * volume;

            if (returnMass <= 0)
                returnMass = 0.0001f;//ckrinke: Mass must be greater then zero.
//            else if (returnMass > _parent_scene.maximumMassObject)
//                returnMass = _parent_scene.maximumMassObject;




            // Recursively calculate mass
            bool HasChildPrim = false;
            lock (childrenPrim)
            {
                if (childrenPrim.Count > 0)
                {
                    HasChildPrim = true;
                }

            }
            if (HasChildPrim)
            {
                AuroraODEPrim[] childPrimArr = new AuroraODEPrim[0];

                lock (childrenPrim)
                    childPrimArr = childrenPrim.ToArray();

                for (int i = 0; i < childPrimArr.Length; i++)
                {
                    if (childPrimArr[i] != null && !childPrimArr[i].m_taintremove)
                        returnMass += childPrimArr[i].CalculateMass();
                    // failsafe, this shouldn't happen but with OpenSim, you never know :)
                    if (i > 256)
                        break;
                }
            }
            if (returnMass > _parent_scene.maximumMassObject)
                returnMass = _parent_scene.maximumMassObject;
            return returnMass;
        }// end CalculateMass

        #endregion

        public void setMass()
        {
            if (Body != (IntPtr) 0)
            {
                Mass = CalculateMass();

                //m_log.Info("[PHYSICS]: New Mass: " + newmass.ToString());
                d.MassSetBoxTotal(out pMass, Mass, _size.X, _size.Y, _size.Z);
                d.BodySetMass(Body, ref pMass);
            }
        }

        public void disableBody()
        {
            //this kills the body so things like 'mesh' can re-create it.
            lock (this)
            {
                if (!childPrim)
                {
                    if (Body != IntPtr.Zero)
                    {
                        m_vehicle.Disable(this);
                        _parent_scene.remActivePrim(this);
                        m_collisionCategories &= ~CollisionCategories.Body;
                        m_collisionFlags &= ~(CollisionCategories.Wind | CollisionCategories.Land);

                        if (prim_geom != IntPtr.Zero)
                        {
                            d.GeomSetCategoryBits(prim_geom, (int)m_collisionCategories);
                            d.GeomSetCollideBits(prim_geom, (int)m_collisionFlags);
                        }

                        d.BodyDestroy(Body);
                        lock (childrenPrim)
                        {
                            if (childrenPrim.Count > 0)
                            {
                                foreach (AuroraODEPrim prm in childrenPrim)
                                {
                                    _parent_scene.remActivePrim(prm);
                                    prm.Body = IntPtr.Zero;
                                }
                            }
                        }
                        Body = IntPtr.Zero;
                    }
                }
                else
                {
                    _parent_scene.remActivePrim(this);
                    
                    m_collisionCategories &= ~CollisionCategories.Body;
                    m_collisionFlags &= ~(CollisionCategories.Wind | CollisionCategories.Land);

                    if (prim_geom != IntPtr.Zero)
                    {
                        d.GeomSetCategoryBits(prim_geom, (int)m_collisionCategories);
                        d.GeomSetCollideBits(prim_geom, (int)m_collisionFlags);
                    }

                    
                    Body = IntPtr.Zero;
                }
            }
            m_disabled = true;
            m_collisionscore = 0;
        }

        private static Dictionary<IMesh, IntPtr> m_MeshToTriMeshMap = new Dictionary<IMesh, IntPtr>();

        public void setMesh(AuroraODEPhysicsScene parent_scene, IMesh mesh)
        {
            // This sleeper is there to moderate how long it takes between
            // setting up the mesh and pre-processing it when we get rapid fire mesh requests on a single object

            //Thread.Sleep(10);

            //Kill Body so that mesh can re-make the geom
            if (IsPhysical && Body != IntPtr.Zero)
            {
                if (childPrim)
                {
                    if (_parent != null)
                    {
                        AuroraODEPrim parent = (AuroraODEPrim)_parent;
                        parent.ChildDelink(this);
                    }
                }
                else
                {
                    disableBody();
                }
            }

            IntPtr vertices, indices;
            int vertexCount, indexCount;
            int vertexStride, triStride;
            mesh.getVertexListAsPtrToFloatArray(out vertices, out vertexStride, out vertexCount); // Note, that vertices are fixed in unmanaged heap
            mesh.getIndexListAsPtrToIntArray(out indices, out triStride, out indexCount); // Also fixed, needs release after usage

            mesh.releaseSourceMeshData(); // free up the original mesh data to save memory
            if (m_MeshToTriMeshMap.ContainsKey(mesh))
            {
                _triMeshData = m_MeshToTriMeshMap[mesh];
            }
            else
            {
                _triMeshData = d.GeomTriMeshDataCreate();

                d.GeomTriMeshDataBuildSimple(_triMeshData, vertices, vertexStride, vertexCount, indices, indexCount, triStride);
                d.GeomTriMeshDataPreprocess(_triMeshData);
                m_MeshToTriMeshMap[mesh] = _triMeshData;
            }

            _parent_scene.waitForSpaceUnlock(m_targetSpace);
            try
            {
                if (prim_geom == IntPtr.Zero)
                {
                    SetGeom(d.CreateTriMesh(m_targetSpace, _triMeshData, parent_scene.triCallback, null, null));
                }
            }
            catch (AccessViolationException)
            {
                m_log.Error("[PHYSICS]: MESH LOCKED");
                return;
            }


           // if (IsPhysical && Body == (IntPtr) 0)
           // {
                // Recreate the body
          //     m_interpenetrationcount = 0;
           //     m_collisionscore = 0;

           //     enableBody();
           // }
        }

        public void ProcessTaints(float timestep)
        {
//Console.WriteLine("ProcessTaints for " + m_primName);
            if (m_taintadd)
            {
                changeadd(timestep);
            }

            if (m_frozen)
                return;
            
            if (prim_geom != IntPtr.Zero)
            {
                 if (!_position.ApproxEquals(m_taintposition, 0f))
                        changemove(timestep);

                 if (m_taintrot != _orientation)
                 {
                    if (childPrim && IsPhysical)    // For physical child prim...
                    {
                        rotate(timestep);
                        // KF: ODE will also rotate the parent prim!
                        // so rotate the root back to where it was
                        AuroraODEPrim parent = (AuroraODEPrim)_parent;
                        parent.rotate(timestep);
                    }
                    else
                    {
                        //Just rotate the prim
                        rotate(timestep);
                    }
                }
                //
            
                if (m_taintPhysics != m_isphysical && !(m_taintparent != _parent))
                    changePhysicsStatus(timestep);
                //

                if (!_size.ApproxEquals(m_taintsize,0f))
                    changesize(timestep);
                //

                if (m_taintshape)
                    changeshape(timestep);
                //

                if (m_taintforce)
                    changeAddForce(timestep);

                if (m_taintaddangularforce)
                    changeAddAngularForce(timestep);

                if (!m_taintTorque.ApproxEquals(Vector3.Zero, 0.001f))
                    changeSetTorque(timestep);

                if (m_taintdisable)
                    changedisable(timestep);

                if (m_taintselected != m_isSelected)
                    changeSelectedStatus(timestep);

                if (!m_taintVelocity.ApproxEquals(Vector3.Zero, 0.001f))
                    changevelocity(timestep);

                if (m_taintparent != _parent)
                    changelink(timestep);

                if (m_taintCollidesWater != m_collidesWater)
                    changefloatonwater(timestep);

                if (m_needsTaintAngularLock)
                    changeAngularLock(timestep);
 
            }
            else
            {
                m_frozen = true;
                m_log.Error("[PHYSICS]: The scene reused a disposed PhysActor! *waves finger*, Don't be evil.  A couple of things can cause this.   An improper prim breakdown(be sure to set prim_geom to zero after d.GeomDestroy!   An improper buildup (creating the geom failed).   Or, the Scene Reused a physics actor after disposing it.)");
            }
        }


        private void changeAngularLock(float timestep)
        {
            // do we have a Physical object?
            if (Body != IntPtr.Zero)
            {
                //Check that we have a Parent
                //If we have a parent then we're not authorative here
                if (_parent == null)
                {
                    if (!m_taintAngularLock.ApproxEquals(Vector3.One, 0f))
                    {
                        //d.BodySetFiniteRotationMode(Body, 0);
                        //d.BodySetFiniteRotationAxis(Body,m_taintAngularLock.X,m_taintAngularLock.Y,m_taintAngularLock.Z);
                        createAMotor(m_taintAngularLock);
                    }
                    else
                    {
                        if (Amotor != IntPtr.Zero)
                        {
                            d.JointDestroy(Amotor);
                            Amotor = IntPtr.Zero;
                        }
                    }
                }
            }
            // Store this for later in case we get turned into a separate body
            m_angularlock = m_taintAngularLock;
            m_needsTaintAngularLock = false;
        }

        private void changelink(float timestep)
        {
            // If the newly set parent is not null
            // create link
            if (_parent == null && m_taintparent != null)
            {
                if (m_taintparent.PhysicsActorType == (int)ActorTypes.Prim)
                {
                    AuroraODEPrim obj = (AuroraODEPrim)m_taintparent;
                    //obj.disableBody();
//Console.WriteLine("changelink calls ParentPrim");
                    obj.ParentPrim(this);

                    /*
                    if (obj.Body != (IntPtr)0 && Body != (IntPtr)0 && obj.Body != Body)
                    {
                        _linkJointGroup = d.JointGroupCreate(0);
                        m_linkJoint = d.JointCreateFixed(_parent_scene.world, _linkJointGroup);
                        d.JointAttach(m_linkJoint, obj.Body, Body);
                        d.JointSetFixed(m_linkJoint);
                    }
                     */
                }
            }
            // If the newly set parent is null
            // destroy link
            else if (_parent != null && m_taintparent == null)
            {
//Console.WriteLine("  changelink B");
            
                if (_parent is AuroraODEPrim)
                {
                    AuroraODEPrim obj = (AuroraODEPrim)_parent;
                    obj.ChildDelink(this);
                    childPrim = false;
                    //_parent = null;
                }
                
                /*
                    if (Body != (IntPtr)0 && _linkJointGroup != (IntPtr)0)
                    d.JointGroupDestroy(_linkJointGroup);
                        
                    _linkJointGroup = (IntPtr)0;
                    m_linkJoint = (IntPtr)0;
                */
            }
 
            _parent = m_taintparent;
            m_taintPhysics = m_isphysical;
        }

        // I'm the parent
        // prim is the child
        public void ParentPrim(AuroraODEPrim prim)
        {
//Console.WriteLine("ParentPrim  " + m_primName);
            if (this.m_localID != prim.m_localID)
            {
                if (Body == IntPtr.Zero)
                {
                    Body = d.BodyCreate(_parent_scene.world);
                    setMass();
                }
                if (Body != IntPtr.Zero)
                {
                    lock (childrenPrim)
                    {
                        if (!childrenPrim.Contains(prim))
                        {
//Console.WriteLine("childrenPrim.Add " + prim);
                            childrenPrim.Add(prim);
                            
                            foreach (AuroraODEPrim prm in childrenPrim)
                            {
                                d.Mass m2;
                                d.MassSetZero(out m2);
                                prim.Mass = prim.CalculateMass();
                                d.MassSetBoxTotal(out m2, prim.Mass, prm._size.X, prm._size.Y, prm._size.Z);


                                d.Quaternion quat = new d.Quaternion();
                                quat.W = prm._orientation.W;
                                quat.X = prm._orientation.X;
                                quat.Y = prm._orientation.Y;
                                quat.Z = prm._orientation.Z;

                                d.Matrix3 mat = new d.Matrix3();
                                d.RfromQ(out mat, ref quat);
                                d.MassRotate(ref m2, ref mat);
                                d.MassTranslate(ref m2, Position.X - prm.Position.X, Position.Y - prm.Position.Y, Position.Z - prm.Position.Z);
                                d.MassAdd(ref pMass, ref m2);
                            }
                            foreach (AuroraODEPrim prm in childrenPrim)
                            {
                       
                                prm.m_collisionCategories |= CollisionCategories.Body;
                                prm.m_collisionFlags |= (CollisionCategories.Land | CollisionCategories.Wind);

                                if (prm.prim_geom == IntPtr.Zero)
                                {
                                    m_log.Warn("[PHYSICS]: Unable to link one of the linkset elements.  No geom yet");
                                    continue;
                                }
//Console.WriteLine(" GeomSetCategoryBits 1: " + prm.prim_geom + " - " + (int)prm.m_collisionCategories + " for " + m_primName);
                                d.GeomSetCategoryBits(prm.prim_geom, (int)prm.m_collisionCategories);
                                d.GeomSetCollideBits(prm.prim_geom, (int)prm.m_collisionFlags);


                                d.Quaternion quat = new d.Quaternion();
                                quat.W = prm._orientation.W;
                                quat.X = prm._orientation.X;
                                quat.Y = prm._orientation.Y;
                                quat.Z = prm._orientation.Z;

                                d.Matrix3 mat = new d.Matrix3();
                                d.RfromQ(out mat, ref quat);
                                if (Body != IntPtr.Zero)
                                {
                                    d.GeomSetBody(prm.prim_geom, Body);
                                    prm.childPrim = true;
                                    d.GeomSetOffsetWorldPosition(prm.prim_geom, prm.Position.X , prm.Position.Y, prm.Position.Z);
                                    //d.GeomSetOffsetPosition(prim.prim_geom,
                                    //    (Position.X - prm.Position.X) - pMass.c.X,
                                    //    (Position.Y - prm.Position.Y) - pMass.c.Y,
                                    //    (Position.Z - prm.Position.Z) - pMass.c.Z);
                                    d.GeomSetOffsetWorldRotation(prm.prim_geom, ref mat);
                                    //d.GeomSetOffsetRotation(prm.prim_geom, ref mat);
                                    d.MassTranslate(ref pMass, -pMass.c.X, -pMass.c.Y, -pMass.c.Z);
                                    d.BodySetMass(Body, ref pMass);
                                }
                                else
                                {
                                    m_log.Debug("[PHYSICS]:I ain't got no boooooooooddy, no body");
                                }


                                prm.m_interpenetrationcount = 0;
                                prm.m_collisionscore = 0;
                                prm.m_disabled = false;

                                // The body doesn't already have a finite rotation mode set here
                                if ((!m_angularlock.ApproxEquals(Vector3.One, 0f)) && _parent == null)
                                {
                                    prm.createAMotor(m_angularlock);
                                }
                                prm.Body = Body;
                                _parent_scene.addActivePrim(prm);
                            }
                            m_collisionCategories |= CollisionCategories.Body;
                            m_collisionFlags |= (CollisionCategories.Land | CollisionCategories.Wind);

//Console.WriteLine("GeomSetCategoryBits 2: " + prim_geom + " - " + (int)m_collisionCategories + " for " + m_primName);
                            d.GeomSetCategoryBits(prim_geom, (int)m_collisionCategories);
//Console.WriteLine(" Post GeomSetCategoryBits 2");
                            d.GeomSetCollideBits(prim_geom, (int)m_collisionFlags);


                            d.Quaternion quat2 = new d.Quaternion();
                            quat2.W = _orientation.W;
                            quat2.X = _orientation.X;
                            quat2.Y = _orientation.Y;
                            quat2.Z = _orientation.Z;

                            d.Matrix3 mat2 = new d.Matrix3();
                            d.RfromQ(out mat2, ref quat2);
                            d.GeomSetBody(prim_geom, Body);
                            d.GeomSetOffsetWorldPosition(prim_geom, Position.X - pMass.c.X, Position.Y - pMass.c.Y, Position.Z - pMass.c.Z);
                            //d.GeomSetOffsetPosition(prim.prim_geom,
                            //    (Position.X - prm.Position.X) - pMass.c.X,
                            //    (Position.Y - prm.Position.Y) - pMass.c.Y,
                            //    (Position.Z - prm.Position.Z) - pMass.c.Z);
                            //d.GeomSetOffsetRotation(prim_geom, ref mat2);
                            d.MassTranslate(ref pMass, -pMass.c.X, -pMass.c.Y, -pMass.c.Z);
                            d.BodySetMass(Body, ref pMass);

                            d.BodySetAutoDisableFlag(Body, true);
                            d.BodySetAutoDisableSteps(Body, body_autodisable_frames);


                            m_interpenetrationcount = 0;
                            m_collisionscore = 0;
                            m_disabled = false;

                            // The body doesn't already have a finite rotation mode set here
                            if ((!m_angularlock.ApproxEquals(Vector3.One, 0f)) && _parent == null)
                            {
                                createAMotor(m_angularlock);
                            }
                            d.BodySetPosition(Body, Position.X, Position.Y, Position.Z);
                            if (m_vehicle.Type != Vehicle.TYPE_NONE) m_vehicle.Enable(Body, this, _parent_scene);
                            _parent_scene.addActivePrim(this);
                        }
                    }
                }
            }

        }

        private void ChildSetGeom(AuroraODEPrim odePrim)
        {
            //if (m_isphysical && Body != IntPtr.Zero)
            lock (childrenPrim)
            {
                foreach (AuroraODEPrim prm in childrenPrim)
                {
                    //prm.childPrim = true;
                    prm.disableBody();
                    //prm.m_taintparent = null;
                    //prm._parent = null;
                    //prm.m_taintPhysics = false;
                    //prm.m_disabled = true;
                    //prm.childPrim = false;
                }
            }
            disableBody();


            if (Body != IntPtr.Zero)
            {
                _parent_scene.remActivePrim(this);
            }

            lock (childrenPrim)
            {
                foreach (AuroraODEPrim prm in childrenPrim)
                {
//Console.WriteLine("ChildSetGeom calls ParentPrim");
                    ParentPrim(prm);
                }
            }
            
        }

        private void ChildDelink(AuroraODEPrim odePrim)
        {
            // Okay, we have a delinked child..   need to rebuild the body.
            lock (childrenPrim)
            {
                foreach (AuroraODEPrim prm in childrenPrim)
                {
                    prm.childPrim = true;
                    prm.disableBody();
                    //prm.m_taintparent = null;
                    //prm._parent = null;
                    //prm.m_taintPhysics = false;
                    //prm.m_disabled = true;
                    //prm.childPrim = false;
                }
            }
            disableBody();

            lock (childrenPrim)
            {
 //Console.WriteLine("childrenPrim.Remove " + odePrim);
                childrenPrim.Remove(odePrim);
            }
            
            
            

            if (Body != IntPtr.Zero)
            {
                _parent_scene.remActivePrim(this);
            }

            

            lock (childrenPrim)
            {
                foreach (AuroraODEPrim prm in childrenPrim)
                {
//Console.WriteLine("ChildDelink calls ParentPrim");
                    ParentPrim(prm);
                }
            }

           
        }

        private void changeSelectedStatus(float timestep)
        {
            if (m_taintselected)
            {
                m_collisionCategories = CollisionCategories.Selected;
                m_collisionFlags = (CollisionCategories.Sensor | CollisionCategories.Space);

                // We do the body disable soft twice because 'in theory' a collision could have happened
                // in between the disabling and the collision properties setting
                // which would wake the physical body up from a soft disabling and potentially cause it to fall
                // through the ground.
                
                // NOTE FOR JOINTS: this doesn't always work for jointed assemblies because if you select
                // just one part of the assembly, the rest of the assembly is non-selected and still simulating,
                // so that causes the selected part to wake up and continue moving.

                // even if you select all parts of a jointed assembly, it is not guaranteed that the entire
                // assembly will stop simulating during the selection, because of the lack of atomicity
                // of select operations (their processing could be interrupted by a thread switch, causing
                // simulation to continue before all of the selected object notifications trickle down to
                // the physics engine).

                // e.g. we select 100 prims that are connected by joints. non-atomically, the first 50 are
                // selected and disabled. then, due to a thread switch, the selection processing is
                // interrupted and the physics engine continues to simulate, so the last 50 items, whose
                // selection was not yet processed, continues to simulate. this wakes up ALL of the 
                // first 50 again. then the last 50 are disabled. then the first 50, which were just woken
                // up, start simulating again, which in turn wakes up the last 50.

                if (m_isphysical)
                {
                    disableBodySoft();
                }

                if (prim_geom != IntPtr.Zero)
                {
                    d.GeomSetCategoryBits(prim_geom, (int)m_collisionCategories);
                    d.GeomSetCollideBits(prim_geom, (int)m_collisionFlags);
                }

                if (m_isphysical)
                {
                    disableBodySoft();
                }
            }
            else
            {
                m_collisionCategories = CollisionCategories.Geom;

                if (m_isphysical)
                    m_collisionCategories |= CollisionCategories.Body;

                m_collisionFlags = m_default_collisionFlags;

                if (m_collidesLand)
                    m_collisionFlags |= CollisionCategories.Land;
                if (m_collidesWater)
                    m_collisionFlags |= CollisionCategories.Water;

                if (prim_geom != IntPtr.Zero)
                {
                    d.GeomSetCategoryBits(prim_geom, (int)m_collisionCategories);
                    d.GeomSetCollideBits(prim_geom, (int)m_collisionFlags);
                }
                if (m_isphysical)
                {
                    if (Body != IntPtr.Zero)
                    {
                        d.BodySetLinearVel(Body, 0f, 0f, 0f);
                        d.BodySetForce(Body, 0, 0, 0);
                        enableBodySoft();
                    }
                }
            }

            resetCollisionAccounting();
            m_isSelected = m_taintselected;
        }//end changeSelectedStatus

        public void ResetTaints()
        {
            m_taintposition = _position;
            m_taintrot = _orientation;
            m_taintPhysics = m_isphysical;
            m_taintselected = m_isSelected;
            m_taintsize = _size;
            m_taintshape = false;
            m_taintforce = false;
            m_taintdisable = false;
            m_taintVelocity = Vector3.Zero;
        }

        public void CreateGeom(IntPtr m_targetSpace, IMesh _mesh)
        {
//Console.WriteLine("CreateGeom:");
            if (_mesh != null)
            {
                setMesh(_parent_scene, _mesh);
            }
            else
            {
                if (_pbs.ProfileShape == ProfileShape.HalfCircle && _pbs.PathCurve == (byte)Extrusion.Curve1)
                {
                    if (_size.X == _size.Y && _size.Y == _size.Z && _size.X == _size.Z)
                    {
                        if (((_size.X / 2f) > 0f))
                        {
                            _parent_scene.waitForSpaceUnlock(m_targetSpace);
                            try
                            {
//Console.WriteLine(" CreateGeom 1");
                                SetGeom(d.CreateSphere(m_targetSpace, _size.X / 2));
                            }
                            catch (AccessViolationException)
                            {
                                m_log.Warn("[PHYSICS]: Unable to create physics proxy for object");
                                ode.dunlock(_parent_scene.world);
                                return;
                            }
                        }
                        else
                        {
                            _parent_scene.waitForSpaceUnlock(m_targetSpace);
                            try
                            {
//Console.WriteLine(" CreateGeom 2");
                                SetGeom(d.CreateBox(m_targetSpace, _size.X, _size.Y, _size.Z));
                            }
                            catch (AccessViolationException)
                            {
                                m_log.Warn("[PHYSICS]: Unable to create physics proxy for object");
                                ode.dunlock(_parent_scene.world);
                                return;
                            }
                        }
                    }
                    else
                    {
                        _parent_scene.waitForSpaceUnlock(m_targetSpace);
                        try
                        {
//Console.WriteLine("  CreateGeom 3");
                            SetGeom(d.CreateBox(m_targetSpace, _size.X, _size.Y, _size.Z));
                        }
                        catch (AccessViolationException)
                        {
                            m_log.Warn("[PHYSICS]: Unable to create physics proxy for object");
                            ode.dunlock(_parent_scene.world);
                            return;
                        }
                    }
                }

                else
                {
                    _parent_scene.waitForSpaceUnlock(m_targetSpace);
                    try
                    {
//Console.WriteLine("  CreateGeom 4");
                        SetGeom(d.CreateBox(m_targetSpace, _size.X, _size.Y, _size.Z));
                    }
                    catch (AccessViolationException)
                    {
                        m_log.Warn("[PHYSICS]: Unable to create physics proxy for object");
                        ode.dunlock(_parent_scene.world);
                        return;
                    }
                }
            }
        }

        public void changeadd(float timestep)
        {
            int[] iprimspaceArrItem = _parent_scene.calculateSpaceArrayItemFromPos(_position);
            IntPtr targetspace = _parent_scene.calculateSpaceForGeom(_position);

            if (targetspace == IntPtr.Zero)
                targetspace = _parent_scene.createprimspace(iprimspaceArrItem[0], iprimspaceArrItem[1]);

            m_targetSpace = targetspace;

            if (_mesh == null)
            {
                if (_parent_scene.needsMeshing(_pbs))
                {
                    // Don't need to re-enable body..   it's done in SetMesh
                    _mesh = _parent_scene.mesher.CreateMesh(m_primName, _pbs, _size, _parent_scene.meshSculptLOD, IsPhysical);
                    // createmesh returns null when it's a shape that isn't a cube.
                   // m_log.Debug(m_localID);
                }
            }


            lock (_parent_scene.OdeLock)
            {
//Console.WriteLine("changeadd 1");
                CreateGeom(m_targetSpace, _mesh);

                if (prim_geom != IntPtr.Zero)
                {
                    d.GeomSetPosition(prim_geom, _position.X, _position.Y, _position.Z);
                    d.Quaternion myrot = new d.Quaternion();
                    myrot.X = _orientation.X;
                    myrot.Y = _orientation.Y;
                    myrot.Z = _orientation.Z;
                    myrot.W = _orientation.W;
                    d.GeomSetQuaternion(prim_geom, ref myrot);
                }

                if (m_isphysical && Body == IntPtr.Zero)
                {
                    enableBody();
                }
            }

            _parent_scene.geom_name_map[prim_geom] = this.m_primName;
            _parent_scene.actor_name_map[prim_geom] = (PhysicsActor)this;

            changeSelectedStatus(timestep);

            m_taintadd = false;
        }

        public void changemove(float timestep)
        {
            if (m_isphysical)
            {
                
                if (!m_disabled && !m_taintremove && !childPrim)
                {
                    if (Body == IntPtr.Zero)
                        enableBody();
                    //Prim auto disable after 20 frames,
                    //if you move it, re-enable the prim manually.
                    if (_parent != null)
                    {
                        if (m_linkJoint != IntPtr.Zero)
                        {
                            d.JointDestroy(m_linkJoint);
                            m_linkJoint = IntPtr.Zero;
                        }
                    }
                    if (Body != IntPtr.Zero)
                    {
                        d.BodySetPosition(Body, _position.X, _position.Y, _position.Z);

                        if (_parent != null)
                        {
                            AuroraODEPrim odParent = (AuroraODEPrim)_parent;
                            if (Body != (IntPtr)0 && odParent.Body != (IntPtr)0 && Body != odParent.Body)
                            {
// KF: Fixed Joints were removed? Anyway - this Console.WriteLine does not show up, so routine is not used??
Console.WriteLine(" JointCreateFixed");
                                m_linkJoint = d.JointCreateFixed(_parent_scene.world, _linkJointGroup);
                                d.JointAttach(m_linkJoint, Body, odParent.Body);
                                d.JointSetFixed(m_linkJoint);
                            }
                        }
                        d.BodyEnable(Body);
                    }
                    else
                    {
                        m_log.Warn("[PHYSICS]: Body Still null after enableBody().  This is a crash scenario.");
                    }
                }
                //else
               // {
                    //m_log.Debug("[BUG]: race!");
                //}
            }
            else
            {
                // string primScenAvatarIn = _parent_scene.whichspaceamIin(_position);
                // int[] arrayitem = _parent_scene.calculateSpaceArrayItemFromPos(_position);
                _parent_scene.waitForSpaceUnlock(m_targetSpace);

                IntPtr tempspace = _parent_scene.recalculateSpaceForGeom(prim_geom, _position, m_targetSpace);
                m_targetSpace = tempspace;

                _parent_scene.waitForSpaceUnlock(m_targetSpace);
                if (prim_geom != IntPtr.Zero)
                {
                    d.GeomSetPosition(prim_geom, _position.X, _position.Y, _position.Z);

                    _parent_scene.waitForSpaceUnlock(m_targetSpace);
                    d.SpaceAdd(m_targetSpace, prim_geom);
                }
            }

            changeSelectedStatus(timestep);

            resetCollisionAccounting();
            m_taintposition = _position;
        }

        public void Move(float timestep)
        {
            if (m_frozen)
                return;
            float fx = 0;
            float fy = 0;
            float fz = 0;

                
            if (IsPhysical && (Body != IntPtr.Zero) && !m_isSelected && !childPrim)        // KF: Only move root prims.
            {
                if (m_vehicle.Type != Vehicle.TYPE_NONE)
                {
                    // 'VEHICLES' are dealt with in ODEDynamics.cs
                    m_vehicle.Step(Body, timestep, _parent_scene, this);
                    d.Vector3 vel = d.BodyGetLinearVel(Body);
                    _velocity = new Vector3(vel.X, vel.Y, vel.Z);
                    d.Vector3 pos = d.BodyGetPosition(Body);
                    _position = new Vector3(pos.X, pos.Y, pos.Z);
                    _zeroFlag = false;
                }
                else
                {
                    //Console.WriteLine("Move " +  m_primName);
                    //if (!d.BodyIsEnabled(Body)) d.BodyEnable(Body); // KF add 161009
                    // NON-'VEHICLES' are dealt with here
                    //float PID_P = 900.0f;

                    float m_mass = Mass;

                    //                    fz = 0f;
                    //m_log.Info(m_collisionFlags.ToString());


                    //KF: m_buoyancy should be set by llSetBuoyancy() for non-vehicle.
                    // would come from SceneObjectPart.cs, public void SetBuoyancy(float fvalue) , PhysActor.Buoyancy = fvalue; ??
                    // m_buoyancy: (unlimited value) <0=Falls fast; 0=1g; 1=0g; >1 = floats up 
                    // gravityz multiplier = 1 - m_buoyancy
                    if (!_parent_scene.UsePointGravity)
                    {
                        if (!testRealGravity)
                        {
                            fx = _parent_scene.gravityx * (1.0f - m_buoyancy) * m_mass;
                            fy = _parent_scene.gravityy * (1.0f - m_buoyancy) * m_mass;
                            fz = _parent_scene.gravityz * (1.0f - m_buoyancy) * m_mass;
                        }
                        else
                        {
                            fx = _parent_scene.gravityx * -1 * (m_buoyancy);
                            fy = _parent_scene.gravityy * -1 * (m_buoyancy);
                            fz = _parent_scene.gravityz * -1 * (m_buoyancy);
                        }
                    }
                    else
                    {
                        //Set up point gravity for this object
                        Vector3 cog = _parent_scene.PointOfGravity;
                        if (cog.X != 0)
                            fx = (cog.X - Position.X) * m_mass;
                        if (cog.Y != 0)
                            fy = (cog.Y - Position.Y) * m_mass;
                        if (cog.Z != 0)
                            fz = (cog.Z - Position.Z) * m_mass;
                    }

                    d.Vector3 vel = d.BodyGetLinearVel(Body);
                    d.Vector3 force = d.BodyGetForce(Body);
                    d.Vector3 angvel = d.BodyGetAngularVel(Body);

                    #region PID
                    if (m_usePID)
                    {
                        //Console.WriteLine("PID " +  m_primName);
                        // KF - this is for object move? eg. llSetPos() ?
                        //if (!d.BodyIsEnabled(Body))
                        //d.BodySetForce(Body, 0f, 0f, 0f);
                        // If we're using the PID controller, then we have no gravity
                        //fz = (-1 * _parent_scene.gravityz) * m_mass;     //KF: ?? Prims have no global gravity,so simply...
                        fz = 0f;

                        //  no lock; for now it's only called from within Simulate()

                        // If the PID Controller isn't active then we set our force
                        // calculating base velocity to the current position

                        if ((m_PIDTau < 1) && (m_PIDTau != 0))
                        {
                            //PID_G = PID_G / m_PIDTau;
                            m_PIDTau = 1;
                        }

                        if ((PID_G - m_PIDTau) <= 0)
                        {
                            PID_G = m_PIDTau + 1;
                        }
                        //PidStatus = true;

                        // PhysicsVector vec = new PhysicsVector();

                        d.Vector3 pos = d.BodyGetPosition(Body);
                        _target_velocity =
                            new Vector3(
                                (m_PIDTarget.X - pos.X) * ((/*PID_G - */m_PIDTau) * timestep),
                                (m_PIDTarget.Y - pos.Y) * ((/*PID_G - */m_PIDTau) * timestep),
                                (m_PIDTarget.Z - pos.Z) * ((/*PID_G - */m_PIDTau) * timestep)
                                );

                        //  if velocity is zero, use position control; otherwise, velocity control

                        if (_target_velocity.ApproxEquals(Vector3.Zero, 0.1f))
                        {
                            //  keep track of where we stopped.  No more slippin' & slidin'

                            // We only want to deactivate the PID Controller if we think we want to have our surrogate
                            // react to the physics scene by moving it's position.
                            // Avatar to Avatar collisions
                            // Prim to avatar collisions

                            //fx = (_target_velocity.X - vel.X) * (PID_D) + (_zeroPosition.X - pos.X) * (PID_P * 2);
                            //fy = (_target_velocity.Y - vel.Y) * (PID_D) + (_zeroPosition.Y - pos.Y) * (PID_P * 2);
                            //fz = fz + (_target_velocity.Z - vel.Z) * (PID_D) + (_zeroPosition.Z - pos.Z) * PID_P;
                            d.BodySetPosition(Body, m_PIDTarget.X, m_PIDTarget.Y, m_PIDTarget.Z);
                            if (!m_angularlock.ApproxEquals(Vector3.One, 0.003f) &&
                                Amotor != IntPtr.Zero)
                            {
                                
                            }
                            d.BodySetLinearVel(Body, 0, 0, 0);
                            d.BodyAddForce(Body, 0, 0, fz);
                            return;
                        }
                        else
                        {
                            _zeroFlag = false;

                            // We're flying and colliding with something
                            fx = ((_target_velocity.X) - vel.X) * (PID_D);
                            fy = ((_target_velocity.Y) - vel.Y) * (PID_D);

                            // vec.Z = (_target_velocity.Z - vel.Z) * PID_D + (_zeroPosition.Z - pos.Z) * PID_P;

                            fz = fz + ((_target_velocity.Z - vel.Z) * (PID_D) * m_mass);
                        }
                    }        // end if (m_usePID)
                    #endregion
                    #region Hover PID
                    // Hover PID Controller needs to be mutually exlusive to MoveTo PID controller
                    if (m_useHoverPID && !m_usePID)
                    {
                        //Console.WriteLine("Hover " +  m_primName);

                        // If we're using the PID controller, then we have no gravity
                        fx = (-1 * _parent_scene.gravityx) * m_mass;
                        fy = (-1 * _parent_scene.gravityy) * m_mass;
                        fz = (-1 * _parent_scene.gravityz) * m_mass;

                        //  no lock; for now it's only called from within Simulate()

                        // If the PID Controller isn't active then we set our force
                        // calculating base velocity to the current position

                        if ((m_PIDTau < 1))
                        {
                            PID_G = PID_G / m_PIDTau;
                        }

                        if ((PID_G - m_PIDTau) <= 0)
                        {
                            PID_G = m_PIDTau + 1;
                        }


                        // Where are we, and where are we headed?
                        d.Vector3 pos = d.BodyGetPosition(Body);


                        //    Non-Vehicles have a limited set of Hover options.
                        // determine what our target height really is based on HoverType
                        switch (m_PIDHoverType)
                        {
                            case PIDHoverType.Ground:
                                m_groundHeight = _parent_scene.GetTerrainHeightAtXY(pos.X, pos.Y);
                                m_targetHoverHeight = m_groundHeight + m_PIDHoverHeight;
                                break;
                            case PIDHoverType.GroundAndWater:
                                m_groundHeight = _parent_scene.GetTerrainHeightAtXY(pos.X, pos.Y);
                                m_waterHeight = _parent_scene.GetWaterLevel();
                                if (m_groundHeight > m_waterHeight)
                                {
                                    m_targetHoverHeight = m_groundHeight + m_PIDHoverHeight;
                                }
                                else
                                {
                                    m_targetHoverHeight = m_waterHeight + m_PIDHoverHeight;
                                }
                                break;

                        }     // end switch (m_PIDHoverType)


                        _target_velocity =
                            new Vector3(0.0f, 0.0f,
                                (m_targetHoverHeight - pos.Z) * ((PID_G - m_PIDHoverTau) * timestep)
                                );

                        //  if velocity is zero, use position control; otherwise, velocity control

                        if (_target_velocity.ApproxEquals(Vector3.Zero, 0.1f))
                        {
                            //  keep track of where we stopped.  No more slippin' & slidin'

                            // We only want to deactivate the PID Controller if we think we want to have our surrogate
                            // react to the physics scene by moving it's position.
                            // Avatar to Avatar collisions
                            // Prim to avatar collisions

                            d.BodySetPosition(Body, pos.X, pos.Y, m_targetHoverHeight);
                            d.BodySetLinearVel(Body, vel.X, vel.Y, 0);
                            d.BodyAddForce(Body, 0, 0, fz);
                            return;
                        }
                        else
                        {
                            _zeroFlag = false;

                            // We're flying and colliding with something
                            fz = fz + ((_target_velocity.Z - vel.Z) * (PID_D) * m_mass);
                        }
                    }
                    #endregion

                    fx *= m_mass;
                    fy *= m_mass;

                    fx += m_force.X;
                    fy += m_force.Y;
                    fz += m_force.Z;

                    //m_log.Info("[OBJPID]: X:" + fx.ToString() + " Y:" + fy.ToString() + " Z:" + fz.ToString());
                    if (fx != 0 || fy != 0 || fz != 0)
                    {
                        // 35n times the mass per second applied maximum.
                        float nmax = 35f * m_mass;
                        float nmin = -35f * m_mass;


                        if (fx > nmax)
                            fx = nmax;
                        if (fx < nmin)
                            fx = nmin;
                        if (fy > nmax)
                            fy = nmax;
                        if (fy < nmin)
                            fy = nmin;

                        if (!m_angularlock.ApproxEquals(Vector3.One, 0.003f) &&
                                Amotor != IntPtr.Zero)
                        {
                            d.JointSetAMotorParam(Amotor, (int)dParam.LowStop, 0.001f);
                            d.JointSetAMotorParam(Amotor, (int)dParam.LoStop3, 0.0001f);
                            d.JointSetAMotorParam(Amotor, (int)dParam.LoStop2, 0.0001f);
                            d.JointSetAMotorParam(Amotor, (int)dParam.HiStop, 0.0001f);
                            d.JointSetAMotorParam(Amotor, (int)dParam.HiStop3, 0.0001f);
                            d.JointSetAMotorParam(Amotor, (int)dParam.HiStop2, 0.0001f);
                        }

                        if (vel.Z < -30)
                        {
                            vel.Z = -30;
                        }

                        bool disabled = false;

                        if (_parent_scene.m_DisableSlowPrims)
                        {
                            if (((float)fz == (float)(_parent_scene.gravityz * m_mass)) &&
                                (vel.X < 0.01 || vel.Y < 0.01 || vel.Z < 0.0001) &&
                                m_forcelist.Count == 0)
                            {
                                if (Math.Abs(vel.X) < 0.0001 || Math.Abs(vel.Y) < 0.0001 || Math.Abs(vel.Z) < 0.0001)
                                {
                                    Vector3 angvelocity = new Vector3(angvel.X, angvel.Y, angvel.Z);
                                    if (Math.Abs(force.X) < 100 && Math.Abs(force.Y) < 100 && Math.Abs(force.Z) < 100 &&
                                        angvelocity.ApproxEquals(Vector3.Zero, 0.01f) &&
                                        vel.X != 0 && vel.Y != 0 && vel.Z != 0)
                                    {
                                        if (d.BodyIsEnabled(Body))
                                        {
                                            d.BodySetLinearVel(Body, 0, 0, 0);
                                            d.BodySetForce(Body, 0, 0, 0);
                                            d.BodyDisable(Body);
                                            disabled = true;
                                        }
                                    }
                                    else
                                    {
                                        if (!d.BodyIsEnabled(Body))
                                            d.BodyEnable(Body);
                                    }
                                }
                                else
                                {
                                    if (!d.BodyIsEnabled(Body))
                                    {
                                        d.BodyEnable(Body);
                                        fz = 100 * m_mass;
                                    }
                                }
                            }
                        }

                        if (!disabled)
                        {
                            if (!d.BodyIsEnabled(Body))
                            {
                                // A physical body at rest on a surface will auto-disable after a while,
                                // this appears to re-enable it incase the surface it is upon vanishes,
                                // and the body should fall again. 
                                d.BodySetLinearVel(Body, 0f, 0f, m_mass);
                                d.BodySetForce(Body, 0, 0, 100 * m_mass);
                                enableBodySoft();
                                vel = d.BodyGetLinearVel(Body);
                            }

                            float Drag = Mass / 2;

                            if (fx != 0)
                                fx -= Drag;
                            if (fy != 0)
                                fy -= Drag;
                            if (fz < 0)
                                fz += Drag;
                            else
                                fz -= Drag;

                            #region Force List

                            Vector3 iforce = Vector3.Zero;
                            int i = 0;
                            try
                            {
                                for (i = 0; i < m_forcelist.Count; i++)
                                {
                                    iforce = iforce + (m_forcelist[i] * 100);
                                }
                            }
                            catch (IndexOutOfRangeException)
                            {
                                m_forcelist = new List<Vector3>();
                                m_collisionscore = 0;
                                m_interpenetrationcount = 0;
                                m_taintforce = false;
                                return;
                            }
                            catch (ArgumentOutOfRangeException)
                            {
                                m_forcelist = new List<Vector3>();
                                m_collisionscore = 0;
                                m_interpenetrationcount = 0;
                                m_taintforce = false;
                                return;
                            }
                            fx += iforce.X;
                            fy += iforce.Y;
                            fz += iforce.Z;
                            m_forcelist.Clear();

                            #endregion

                            d.BodyAddForce(Body, fx, fy, fz);
                        }
                    }
                }
            }
            else
            {    // is not physical, or is not a body or is selected
              //  _zeroPosition = d.BodyGetPosition(Body);
                return;
//Console.WriteLine("Nothing " +  m_primName);
               
            }
        }

        private d.Quaternion ConvertTodQuat(Quaternion q)
        {
            d.Quaternion dq = new d.Quaternion();
            dq.X = q.X;
            dq.Y = q.Y;
            dq.Z = q.Z;
            dq.W = q.W;
            return dq;
        }

        public void rotate(float timestep)
        {
            d.Quaternion myrot = new d.Quaternion();
            myrot.X = _orientation.X;
            myrot.Y = _orientation.Y;
            myrot.Z = _orientation.Z;
            myrot.W = _orientation.W;
            if (Body != IntPtr.Zero)
            {
                // KF: If this is a root prim do BodySet
                d.BodySetQuaternion(Body, ref myrot);
                if (m_isphysical)
                {
                    if (!m_angularlock.ApproxEquals(Vector3.One, 0f))
                        createAMotor(m_angularlock);
                }
            }
            else
            {
                // daughter prim, do Geom set
                d.GeomSetQuaternion(prim_geom, ref myrot);
            }
            
            resetCollisionAccounting();
            m_taintrot = _orientation;
        }

        private void resetCollisionAccounting()
        {
            m_collisionscore = 0;
            m_interpenetrationcount = 0;
            m_disabled = false;
        }

        public void changedisable(float timestep)
        {
            m_disabled = true;
            if (Body != IntPtr.Zero)
            {
                d.BodyDisable(Body);
                Body = IntPtr.Zero;
            }

            m_taintdisable = false;
        }

        public void changePhysicsStatus(float timestep)
        {
            if (m_isphysical == true)
            {
                if (Body == IntPtr.Zero)
                {
                    if (_pbs.SculptEntry && _parent_scene.meshSculptedPrim)
                    {
                        changeshape(2f);
                    }
                    else
                    {
                        enableBody();
                    }
                }
            }
            else
            {
                if (Body != IntPtr.Zero)
                {
                    if (_pbs.SculptEntry && _parent_scene.meshSculptedPrim)
                    {
                        

                        if (prim_geom != IntPtr.Zero)
                        {
                            try
                            {
                                d.GeomDestroy(prim_geom);
                                prim_geom = IntPtr.Zero;
                                _mesh = null;
                            }
                            catch (System.AccessViolationException)
                            {
                                prim_geom = IntPtr.Zero;
                                m_log.Error("[PHYSICS]: PrimGeom dead");
                            }
                        }
//Console.WriteLine("changePhysicsStatus for " + m_primName);
                        changeadd(2f);
                    }
                    if (childPrim)
                    {
                        if (_parent != null)
                        {
                            AuroraODEPrim parent = (AuroraODEPrim)_parent;
                            parent.ChildDelink(this);
                        }
                    }
                    else
                    {
                        disableBody();
                    }
                }
            }

            changeSelectedStatus(timestep);

            resetCollisionAccounting();
            m_taintPhysics = m_isphysical;
        }

        public void changesize(float timestamp)
        {
            
            string oldname = _parent_scene.geom_name_map[prim_geom];

            if (_size.X <= 0) _size.X = 0.01f;
            if (_size.Y <= 0) _size.Y = 0.01f;
            if (_size.Z <= 0) _size.Z = 0.01f;

            // Cleanup of old prim geometry
            if (_mesh != null)
            {
                // Cleanup meshing here
            }
            //kill body to rebuild
            if (IsPhysical && Body != IntPtr.Zero)
            {
                if (childPrim)
                {
                    if (_parent != null)
                    {
                        AuroraODEPrim parent = (AuroraODEPrim)_parent;
                        parent.ChildDelink(this);
                    }
                }
                else
                {
                    disableBody();
                }
            }
            if (d.SpaceQuery(m_targetSpace, prim_geom))
            {
                _parent_scene.waitForSpaceUnlock(m_targetSpace);
                d.SpaceRemove(m_targetSpace, prim_geom);
            }
            d.GeomDestroy(prim_geom);
            prim_geom = IntPtr.Zero;
            // we don't need to do space calculation because the client sends a position update also.

            // Construction of new prim
            if (_parent_scene.needsMeshing(_pbs))
            {
                float meshlod = _parent_scene.meshSculptLOD;

                if (IsPhysical)
                    meshlod = _parent_scene.MeshSculptphysicalLOD;
                // Don't need to re-enable body..   it's done in SetMesh

                IMesh mesh = null;

                if (_parent_scene.needsMeshing(_pbs))
                    mesh = _parent_scene.mesher.CreateMesh(oldname, _pbs, _size, meshlod, IsPhysical);

                //IMesh mesh = _parent_scene.mesher.CreateMesh(oldname, _pbs, _size, meshlod, IsPhysical);
//Console.WriteLine("changesize 1");
                CreateGeom(m_targetSpace, mesh);

               
            }
            else
            {
                _mesh = null;
//Console.WriteLine("changesize 2");
                CreateGeom(m_targetSpace, _mesh);
            }

            d.GeomSetPosition(prim_geom, _position.X, _position.Y, _position.Z);
            d.Quaternion myrot = new d.Quaternion();
            myrot.X = _orientation.X;
            myrot.Y = _orientation.Y;
            myrot.Z = _orientation.Z;
            myrot.W = _orientation.W;
            d.GeomSetQuaternion(prim_geom, ref myrot);

            //d.GeomBoxSetLengths(prim_geom, _size.X, _size.Y, _size.Z);
            if (IsPhysical && Body == IntPtr.Zero && !childPrim)
            {
                // Re creates body on size.
                // EnableBody also does setMass()
                enableBody();
                d.BodyEnable(Body);
            }

            _parent_scene.geom_name_map[prim_geom] = oldname;

            changeSelectedStatus(timestamp);
            if (childPrim)
            {
                if (_parent is AuroraODEPrim)
                {
                    AuroraODEPrim parent = (AuroraODEPrim)_parent;
                    parent.ChildSetGeom(this);
                }
            }
            resetCollisionAccounting();
            m_taintsize = _size;
        }

       

        public void changefloatonwater(float timestep)
        {
            m_collidesWater = m_taintCollidesWater;

            if (prim_geom != IntPtr.Zero)
            {
                if (m_collidesWater)
                {
                    m_collisionFlags |= CollisionCategories.Water;
                }
                else
                {
                    m_collisionFlags &= ~CollisionCategories.Water;
                }
                d.GeomSetCollideBits(prim_geom, (int)m_collisionFlags);
            }
        }

        public void changeshape(float timestamp)
        {
            string oldname = _parent_scene.geom_name_map[prim_geom];

            // Cleanup of old prim geometry and Bodies
            if (IsPhysical && Body != IntPtr.Zero)
            {
                if (childPrim)
                {
                    if (_parent != null)
                    {
                        AuroraODEPrim parent = (AuroraODEPrim)_parent;
                        parent.ChildDelink(this);
                    }
                }
                else
                {
                    disableBody();
                }
            }
            try
            {
                d.GeomDestroy(prim_geom);
            }
            catch (System.AccessViolationException)
            {
                prim_geom = IntPtr.Zero;
                m_log.Error("[PHYSICS]: PrimGeom dead");
            }
            prim_geom = IntPtr.Zero;
            // we don't need to do space calculation because the client sends a position update also.
            if (_size.X <= 0) _size.X = 0.01f;
            if (_size.Y <= 0) _size.Y = 0.01f;
            if (_size.Z <= 0) _size.Z = 0.01f;
            // Construction of new prim

            if (_parent_scene.needsMeshing(_pbs))
            {
                // Don't need to re-enable body..   it's done in SetMesh
                float meshlod = _parent_scene.meshSculptLOD;

                if (IsPhysical)
                    meshlod = _parent_scene.MeshSculptphysicalLOD;

                IMesh mesh = _parent_scene.mesher.CreateMesh(oldname, _pbs, _size, meshlod, IsPhysical);
                // createmesh returns null when it doesn't mesh.
                CreateGeom(m_targetSpace, mesh);
            }
            else
            {
                _mesh = null;
//Console.WriteLine("changeshape");
                CreateGeom(m_targetSpace, null);
            }

            d.GeomSetPosition(prim_geom, _position.X, _position.Y, _position.Z);
            d.Quaternion myrot = new d.Quaternion();
            //myrot.W = _orientation.w;
            myrot.W = _orientation.W;
            myrot.X = _orientation.X;
            myrot.Y = _orientation.Y;
            myrot.Z = _orientation.Z;
            d.GeomSetQuaternion(prim_geom, ref myrot);

            //d.GeomBoxSetLengths(prim_geom, _size.X, _size.Y, _size.Z);
            if (IsPhysical && Body == IntPtr.Zero)
            {
                // Re creates body on size.
                // EnableBody also does setMass()
                enableBody();
                if (Body != IntPtr.Zero)
                {
                    d.BodyEnable(Body);
                }
            }
            _parent_scene.geom_name_map[prim_geom] = oldname;

            changeSelectedStatus(timestamp);
            if (childPrim)
            {
                if (_parent is AuroraODEPrim)
                {
                    AuroraODEPrim parent = (AuroraODEPrim)_parent;
                    parent.ChildSetGeom(this);
                }
            }
            resetCollisionAccounting();
            m_taintshape = false;
        }

        public void changeAddForce(float timestamp)
        {
            if (!m_isSelected)
            {
                lock (m_forcelist)
                {
                    if (m_vehicle.Type == Vehicle.TYPE_NONE)
                    {
                        //m_log.Info("[PHYSICS]: dequeing forcelist");
                        //if (IsPhysical)
                        //{
                            
                        //}
                        //m_forcelist.Clear();
                    }
                    else
                    {
                        m_vehicle.ProcessForceTaint(m_forcelist);
                    }
                }

                m_collisionscore = 0;
                m_interpenetrationcount = 0;
            }

            m_taintforce = false;

        }



        public void changeSetTorque(float timestamp)
        {
            if (!m_isSelected)
            {
                if (IsPhysical && Body != IntPtr.Zero)
                {
                    d.BodySetTorque(Body, m_taintTorque.X, m_taintTorque.Y, m_taintTorque.Z);
                }
            }

            m_taintTorque = Vector3.Zero;
        }

        public void changeAddAngularForce(float timestamp)
        {
            if (!m_isSelected)
            {
                lock (m_angularforcelist)
                {
                    //m_log.Info("[PHYSICS]: dequeing forcelist");
                    if (IsPhysical)
                    {
                        Vector3 iforce = Vector3.Zero;
                        for (int i = 0; i < m_angularforcelist.Count; i++)
                        {
                            iforce = iforce + (m_angularforcelist[i] * 100);
                        }
                        d.BodyEnable(Body);
                        d.BodyAddTorque(Body, iforce.X, iforce.Y, iforce.Z);
                        
                    }
                    m_angularforcelist.Clear();
                }

                m_collisionscore = 0;
                m_interpenetrationcount = 0;
            }

            m_taintaddangularforce = false;
        }

        private void changevelocity(float timestep)
        {
            if (!m_isSelected)
            {
                Thread.Sleep(20);
                if (IsPhysical)
                {
                    if (Body != IntPtr.Zero)
                    {
                        d.BodySetLinearVel(Body, m_taintVelocity.X, m_taintVelocity.Y, m_taintVelocity.Z);
                    }
                }
                
                //resetCollisionAccounting();
            }
            m_taintVelocity = Vector3.Zero;
        }

        public override bool IsPhysical
        {
            get { return m_isphysical; }
            set { 
                  m_isphysical = value;
                  if (!m_isphysical) // Zero the remembered last velocity
                      m_lastVelocity = Vector3.Zero;
                }
        }

        public void setPrimForRemoval()
        {
            m_taintremove = true;
        }

        public override bool Flying
        {
            // no flying prims for you
            get { return false; }
            set { }
        }

        public override bool IsColliding
        {
            get { return iscolliding; }
            set { iscolliding = value; }
        }

        public override bool CollidingGround
        {
            get { return false; }
            set { return; }
        }

        public override bool CollidingObj
        {
            get { return false; }
            set { return; }
        }

        public override bool ThrottleUpdates
        {
            get { return m_throttleUpdates; }
            set { m_throttleUpdates = value; }
        }

        public override bool Stopped
        {
            get { return _zeroFlag; }
        }

        public override Vector3 Position
        {
            get { return _position; }

            set { _position = value;
                //m_log.Info("[PHYSICS]: " + _position.ToString());
            }
        }

        public override Vector3 Size
        {
            get { return _size; }
            set
            {
                if (value.IsFinite())
                {
                    _size = value;
                }
                else
                {
                    m_log.Warn("[PHYSICS]: Got NaN Size on object");
                }
            }
        }

        private float m_mass = 80;

        public override float Mass
        {
            get
            { 
                if(m_mass == 80)
                    m_mass = CalculateMass();
                return m_mass;
            }
            set
            {
                m_mass = value;
            }
        }

        public override Vector3 Force
        {
            //get { return Vector3.Zero; }
            get { return m_force; }
            set
            {
                if (value.IsFinite())
                {
                    m_force = value;
                }
                else
                {
                    m_log.Warn("[PHYSICS]: NaN in Force Applied to an Object");
                }
            }
        }

        public override int VehicleType
        {
            get { return (int)m_vehicle.Type; }
            set
            {
                if (m_vehicle.Type != Vehicle.TYPE_NONE)
                {
                    m_vehicle.Enable(Body, this, _parent_scene);
                }
                m_vehicle.ProcessTypeChange((Vehicle)value); 
            }
        }

        public override void VehicleFloatParam(int param, float value)
        {
            m_vehicle.ProcessFloatVehicleParam((Vehicle) param, value);
        }

        public override void VehicleVectorParam(int param, Vector3 value)
        {
            m_vehicle.ProcessVectorVehicleParam((Vehicle) param, value);
        }

        public override void VehicleRotationParam(int param, Quaternion rotation)
        {
            m_vehicle.ProcessRotationVehicleParam((Vehicle) param, rotation);
        }

        public override void VehicleFlags(int param, bool remove)
        {
            m_vehicle.ProcessVehicleFlags(param, remove);
        }

        public override void SetCameraPos(Vector3 CameraRotation)
        {
            m_vehicle.ProcessSetCameraPos(CameraRotation);
        }

        public override void SetVolumeDetect(int param)
        {
            lock (_parent_scene.OdeLock)
            {
                m_isVolumeDetect = (param!=0);
            }
        }

        public override Vector3 CenterOfMass
        {
            get { return Vector3.Zero; }
        }

        public override Vector3 GeometricCenter
        {
            get { return Vector3.Zero; }
        }

        public override PrimitiveBaseShape Shape
        {
            set
            {
                _pbs = value;
                m_taintshape = true;
            }
        }

        public override Vector3 Velocity
        {
            get
            {
                // Averate previous velocity with the new one so
                // client object interpolation works a 'little' better
                if (_zeroFlag)
                    return Vector3.Zero;

                Vector3 returnVelocity = Vector3.Zero;
                returnVelocity.X = (m_lastVelocity.X + _velocity.X)/2;
                returnVelocity.Y = (m_lastVelocity.Y + _velocity.Y)/2;
                returnVelocity.Z = (m_lastVelocity.Z + _velocity.Z)/2;
                return returnVelocity;
            }
            set
            {
                if (value.IsFinite())
                {
                    _velocity = value;

                    m_taintVelocity = value;
                    _parent_scene.AddPhysicsActorTaint(this);
                }
                else
                {
                    m_log.Warn("[PHYSICS]: Got NaN Velocity in Object");
                }
            }
        }

        public override Vector3 Torque
        {
            get
            {
                if (!m_isphysical || Body == IntPtr.Zero)
                    return Vector3.Zero;

                return _torque;
            }

            set
            {
                if (value.IsFinite())
                {
                    m_taintTorque = value;
                    _parent_scene.AddPhysicsActorTaint(this);
                }
                else
                {
                    m_log.Warn("[PHYSICS]: Got NaN Torque in Object");
                }
            }
        }

        public override float CollisionScore
        {
            get { return m_collisionscore; }
            set { m_collisionscore = value; }
        }

        public override bool Kinematic
        {
            get { return false; }
            set { }
        }

        public override Quaternion Orientation
        {
            get { return _orientation; }
            set
            {
                if (QuaternionIsFinite(value))
                {
                    _orientation = value;
                }
                else
                    m_log.Warn("[PHYSICS]: Got NaN quaternion Orientation from Scene in Object");

            }
        }

        internal static bool QuaternionIsFinite(Quaternion q)
        {
            if (Single.IsNaN(q.X) || Single.IsInfinity(q.X))
                return false;
            if (Single.IsNaN(q.Y) || Single.IsInfinity(q.Y))
                return false;
            if (Single.IsNaN(q.Z) || Single.IsInfinity(q.Z))
                return false;
            if (Single.IsNaN(q.W) || Single.IsInfinity(q.W))
                return false;
            return true;
        }

        public override Vector3 Acceleration
        {
            get { return _acceleration; }
        }


        public void SetAcceleration(Vector3 accel)
        {
            _acceleration = accel;
        }

        public override void AddForce(Vector3 force, bool pushforce)
        {
            if (force.IsFinite())
            {
                lock (m_forcelist)
                    m_forcelist.Add(force);

                m_taintforce = true;
            }
            else
            {
                m_log.Warn("[PHYSICS]: Got Invalid linear force vector from Scene in Object");
            }
            //m_log.Info("[PHYSICS]: Added Force:" + force.ToString() +  " to prim at " + Position.ToString());
        }

        public override void AddAngularForce(Vector3 force, bool pushforce)
        {
            if (force.IsFinite())
            {
                m_angularforcelist.Add(force);
                m_taintaddangularforce = true;
            }
            else
            {
                m_log.Warn("[PHYSICS]: Got Invalid Angular force vector from Scene in Object");
            }
        }

        public override Vector3 RotationalVelocity
        {
            get
            {
                if (_zeroFlag)
                    return Vector3.Zero;
                m_lastUpdateSent = 5;

                if (m_rotationalVelocity.ApproxEquals(Vector3.Zero, 0.2f))
                    return Vector3.Zero;

                return m_rotationalVelocity;
            }
            set
            {
                if (value.IsFinite())
                {
                    m_rotationalVelocity = value;
                }
                else
                {
                    m_log.Warn("[PHYSICS]: Got NaN RotationalVelocity in Object");
                }
            }
        }

        public override void CrossingFailure()
        {
            m_crossingfailures++;
            if (m_crossingfailures > _parent_scene.geomCrossingFailuresBeforeOutofbounds)
            {
                base.RaiseOutOfBounds(_position);
                return;
            }
            else if (m_crossingfailures == _parent_scene.geomCrossingFailuresBeforeOutofbounds)
            {
                m_log.Warn("[PHYSICS]: Too many crossing failures for: " + m_primName);
            }
        }

        public override float Buoyancy
        {
            get { return m_buoyancy; }
            set { m_buoyancy = value; }
        }

        public override void link(PhysicsActor obj)
        {
            m_taintparent = obj;
        }

        public override void delink()
        {
            m_taintparent = null;
        }

        public override void LockAngularMotion(Vector3 axis)
        {
            // reverse the zero/non zero values for ODE.
            if (axis.IsFinite())
            {
                axis.X = (axis.X > 0) ? 1f : 0f;
                axis.Y = (axis.Y > 0) ? 1f : 0f;
                axis.Z = (axis.Z > 0) ? 1f : 0f;
                m_log.DebugFormat("[axislock]: <{0},{1},{2}>", axis.X, axis.Y, axis.Z);
                m_taintAngularLock = axis;
                m_needsTaintAngularLock = true;
            }
            else
            {
                m_log.Warn("[PHYSICS]: Got NaN locking axis from Scene on Object");
            }
        }

        public void UpdatePositionAndVelocity()
        {
            if (m_frozen)
                return;
            //  no lock; called from Simulate() -- if you call this from elsewhere, gotta lock or do Monitor.Enter/Exit!
            if (_parent == null)
            {
                Vector3 pv = Vector3.Zero;
                bool lastZeroFlag = _zeroFlag;
                if (Body != IntPtr.Zero) // FIXME -> or if it is a joint
                {
                    d.Vector3 vec = d.BodyGetPosition(Body);
                    d.Quaternion ori = d.BodyGetQuaternion(Body);
                    d.Vector3 vel = d.BodyGetLinearVel(Body);
                    d.Vector3 rotvel = d.BodyGetAngularVel(Body);
                    d.Vector3 torque = d.BodyGetTorque(Body);
                    _torque = new Vector3(torque.X, torque.Y, torque.Z);
                    Vector3 l_position = Vector3.Zero;
                    Quaternion l_orientation = Quaternion.Identity;

                    //  kluge to keep things in bounds.  ODE lets dead avatars drift away (they should be removed!)
                    //if (vec.X < 0.0f) { vec.X = 0.0f; if (Body != (IntPtr)0) d.BodySetAngularVel(Body, 0, 0, 0); }
                    //if (vec.Y < 0.0f) { vec.Y = 0.0f; if (Body != (IntPtr)0) d.BodySetAngularVel(Body, 0, 0, 0); }
                    //if (vec.X > 255.95f) { vec.X = 255.95f; if (Body != (IntPtr)0) d.BodySetAngularVel(Body, 0, 0, 0); }
                    //if (vec.Y > 255.95f) { vec.Y = 255.95f; if (Body != (IntPtr)0) d.BodySetAngularVel(Body, 0, 0, 0); }

                    m_lastposition = _position;
                    m_lastorientation = _orientation;

                    l_position.X = vec.X;
                    l_position.Y = vec.Y;
                    l_position.Z = vec.Z;
                    l_orientation.X = ori.X;
                    l_orientation.Y = ori.Y;
                    l_orientation.Z = ori.Z;
                    l_orientation.W = ori.W;

                    if (l_position.X > ((int)_parent_scene.WorldExtents.X - 0.05f) || l_position.X < 0f || l_position.Y > ((int)_parent_scene.WorldExtents.Y - 0.05f) || l_position.Y < 0f)
                    {
                        //base.RaiseOutOfBounds(l_position);

                        if (m_crossingfailures < _parent_scene.geomCrossingFailuresBeforeOutofbounds)
                        {
                            _position = l_position;
                            m_crossingfailures++;
                            if (_parent == null)
                                base.RequestPhysicsterseUpdate();
                            return;
                        }
                        else
                        {
                            m_frozen = true;
                            if (_parent == null)
                                base.RaiseOutOfBounds(l_position);
                            return;
                        }
                    }
                    #region position.Z < 0 ||(l_position.Z > _parent_scene.m_flightCeilingHeight && _parent_scene.m_useFlightCeilingHeight))
                    if (l_position.Z < 0 || (l_position.Z > _parent_scene.m_flightCeilingHeight && _parent_scene.m_useFlightCeilingHeight))
                    {
                        // This is so prim that get lost underground don't fall forever and suck up
                        //
                        // Sim resources and memory.
                        // Disables the prim's movement physics....
                        // It's a hack and will generate a console message if it fails.

                        //IsPhysical = false;
                        if (_parent == null)
                            base.RaiseOutOfBounds(_position);

                        _acceleration.X = 0;
                        _acceleration.Y = 0;
                        _acceleration.Z = 0;

                        _velocity.X = 0;
                        _velocity.Y = 0;
                        _velocity.Z = 0;
                        m_rotationalVelocity.X = 0;
                        m_rotationalVelocity.Y = 0;
                        m_rotationalVelocity.Z = 0;

                        m_throttleUpdates = false;
                        throttleCounter = 0;
                        _zeroFlag = true;
                        m_frozen = true;
                    }
                    #endregion
                    
                    if ((Math.Abs(m_lastposition.X - l_position.X) < 0.005)
                        && (Math.Abs(m_lastposition.Y - l_position.Y) < 0.005)
                        && (Math.Abs(m_lastposition.Z - l_position.Z) < 0.005)
                        && (Math.Abs(_velocity.X - m_lastVelocity.X) < 0.005)
                        && (Math.Abs(_velocity.Y - m_lastVelocity.Y) < 0.005)
                        && (Math.Abs(_velocity.Z - m_lastVelocity.Z) < 0.005) //Accel
                        && (1.0 - Math.Abs(Quaternion.Dot(m_lastorientation, l_orientation)) < 0.0001)
                        && m_vehicle.Type == Vehicle.TYPE_NONE)  // KF 0.01 is far to large
                    {
                        _zeroFlag = true;
                        m_throttleUpdates = false;
                    }
                    else
                    {
                        _zeroFlag = false;
                        m_lastUpdateSent = 5;
                    }

                    if (_zeroFlag)
                    {
                        _velocity.X = 0.0f;
                        _velocity.Y = 0.0f;
                        _velocity.Z = 0.0f;

                        _acceleration.X = 0;
                        _acceleration.Y = 0;
                        _acceleration.Z = 0;

                        m_rotationalVelocity.X = 0;
                        m_rotationalVelocity.Y = 0;
                        m_rotationalVelocity.Z = 0;

                        if (m_lastUpdateSent > 0)
                        {
                            if (throttleCounter > 200 || m_lastUpdateSent >= 5)
                            {
                                if (_parent == null)
                                {
                                    base.RequestPhysicsterseUpdate();
                                    m_lastUpdateSent--;
                                }

                                throttleCounter = 0;
                            }
                            else
                                throttleCounter++;
                        }
                    }
                    else
                    {
                        m_lastVelocity = _velocity;
                        if (m_vehicle.Type == Vehicle.TYPE_NONE)
                        {
                            _position = l_position;

                            _velocity.X = vel.X;
                            _velocity.Y = vel.Y;
                            _velocity.Z = vel.Z;

                            _acceleration = ((_velocity - m_lastVelocity) / 0.1f);
                            //m_log.Info("[PHYSICS]: V1: " + _velocity + " V2: " + m_lastVelocity + " Acceleration: " + _acceleration.ToString());

                            if (_velocity.ApproxEquals(pv, 0.5f))
                                m_rotationalVelocity = pv;
                            else
                                m_rotationalVelocity = new Vector3(rotvel.X, rotvel.Y, rotvel.Z);

                            if (_velocity.ApproxEquals(pv, 0.1f))
                            {
                                if (_velocity != Vector3.Zero)
                                {
                                    _velocity = Vector3.Zero;
                                    _zeroFlag = true;
                                    m_lastUpdateSent = 5;
                                    d.BodySetLinearDamping(Body, 100);
                                }
                                else
                                    d.BodySetForce(Body, 0, 0, 0);
                            }
                            else
                                d.BodySetLinearDamping(Body, 1);
                            if (m_rotationalVelocity != Vector3.Zero && m_rotationalVelocity.ApproxEquals(pv, 0.1f))
                            {
                                m_rotationalVelocity = Vector3.Zero;
                                _zeroFlag = true;
                                m_lastUpdateSent = 5;
                            }
                            d.BodySetLinearVel(Body, _velocity.X,
                                _velocity.Y,
                                _velocity.Z);
                            d.BodySetAngularVel(Body, m_rotationalVelocity.X,
                                m_rotationalVelocity.Y,
                                m_rotationalVelocity.Z);

                            //m_log.Debug("ODE: " + m_rotationalVelocity.ToString());
                        }
                        _orientation.X = ori.X;
                        _orientation.Y = ori.Y;
                        _orientation.Z = ori.Z;
                        _orientation.W = ori.W;
                        if (!m_throttleUpdates || throttleCounter > _parent_scene.geomUpdatesPerThrottledUpdate)
                        {
                            if (_parent == null)
                                base.RequestPhysicsterseUpdate();
                            throttleCounter = 0;
                        }
                        else
                            throttleCounter++;
                    }
                    m_lastposition = l_position;
                }
                else
                {
                    // Not a body..   so Make sure the client isn't interpolating
                    _velocity.X = 0;
                    _velocity.Y = 0;
                    _velocity.Z = 0;

                    _acceleration.X = 0;
                    _acceleration.Y = 0;
                    _acceleration.Z = 0;

                    m_rotationalVelocity.X = 0;
                    m_rotationalVelocity.Y = 0;
                    m_rotationalVelocity.Z = 0;
                    _zeroFlag = true;
                    m_frozen = true;
                }
            }
        }

        public override bool FloatOnWater
        {
            set 
            {
                m_taintCollidesWater = value;
                _parent_scene.AddPhysicsActorTaint(this);
            }
        }

        public override void SetMomentum(Vector3 momentum)
        {
        }

        public override Vector3 PIDTarget 
        {
            get
            {
                return m_PIDTarget;
            }
            set
            {
                if (value.IsFinite())
                {
                    m_PIDTarget = value;
                }
                else
                    m_log.Warn("[PHYSICS]: Got NaN PIDTarget from Scene on Object");
            } 
        }
        public override bool PIDActive { get { return m_usePID; } set { m_usePID = value; } }
        public override float PIDTau { get { return m_PIDTau; } set { m_PIDTau = value; } }

        public override float PIDHoverHeight { set { m_PIDHoverHeight = value; ; } }
        public override bool PIDHoverActive { set { m_useHoverPID = value; } }
        public override PIDHoverType PIDHoverType { set { m_PIDHoverType = value; } }
        public override float PIDHoverTau { set { m_PIDHoverTau = value; } }
        
        public override Quaternion APIDTarget{ set { return; } }

        public override bool APIDActive{ set { return; } }

        public override float APIDStrength{ set { return; } }

        public override float APIDDamping{ set { return; } }


        private void createAMotor(Vector3 axis)
        {
            if (Body == IntPtr.Zero)
                return;

            if (Amotor != IntPtr.Zero)
            {
                d.JointDestroy(Amotor);
                Amotor = IntPtr.Zero;
            }

            float axisnum = 3;

            axisnum = (axisnum - (axis.X + axis.Y + axis.Z));

            // PhysicsVector totalSize = new PhysicsVector(_size.X, _size.Y, _size.Z);


            // Inverse Inertia Matrix, set the X, Y, and/r Z inertia to 0 then invert it again.
            d.Mass objMass;
            d.MassSetZero(out objMass);
            DMassCopy(ref pMass, ref objMass);

            //m_log.DebugFormat("1-{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, ", objMass.I.M00, objMass.I.M01, objMass.I.M02, objMass.I.M10, objMass.I.M11, objMass.I.M12, objMass.I.M20, objMass.I.M21, objMass.I.M22);

            Matrix4 dMassMat = FromDMass(objMass);

            Matrix4 mathmat = Inverse(dMassMat);

            /*
            //m_log.DebugFormat("2-{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, ", mathmat[0, 0], mathmat[0, 1], mathmat[0, 2], mathmat[1, 0], mathmat[1, 1], mathmat[1, 2], mathmat[2, 0], mathmat[2, 1], mathmat[2, 2]);

            mathmat = Inverse(mathmat);


            objMass = FromMatrix4(mathmat, ref objMass);
            //m_log.DebugFormat("3-{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, ", objMass.I.M00, objMass.I.M01, objMass.I.M02, objMass.I.M10, objMass.I.M11, objMass.I.M12, objMass.I.M20, objMass.I.M21, objMass.I.M22);

            mathmat = Inverse(mathmat);
            */
            if (axis.X == 0)
            {
                mathmat.M33 = 50.0000001f;
                //objMass.I.M22 = 0;
            }
            if (axis.Y == 0)
            {
                mathmat.M22 = 50.0000001f;
                //objMass.I.M11 = 0;
            }
            if (axis.Z == 0)
            {
                mathmat.M11 = 50.0000001f;
                //objMass.I.M00 = 0;
            }



            mathmat = Inverse(mathmat);
            objMass = FromMatrix4(mathmat, ref objMass);
            //m_log.DebugFormat("4-{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, ", objMass.I.M00, objMass.I.M01, objMass.I.M02, objMass.I.M10, objMass.I.M11, objMass.I.M12, objMass.I.M20, objMass.I.M21, objMass.I.M22);

            //return;
            if (d.MassCheck(ref objMass))
            {
                d.BodySetMass(Body, ref objMass);
            }
            else
            {
                //m_log.Debug("[PHYSICS]: Mass invalid, ignoring");
            }

            if (axisnum <= 0)
                return;
            // int dAMotorEuler = 1;

            Amotor = d.JointCreateAMotor(_parent_scene.world, IntPtr.Zero);
            d.JointAttach(Amotor, Body, IntPtr.Zero);

            int dAMotorEuler = 1;
            d.JointSetAMotorMode(Amotor, dAMotorEuler);


            d.JointSetAMotorNumAxes(Amotor, (int)axisnum);
            int i = 0;
            if (axis.X == 0)
            {
                d.JointSetAMotorAxis(Amotor, i, 0, 1, 0, 0);
                i++;
            }

            if (axis.Y == 0)
            {
                d.JointSetAMotorAxis(Amotor, i, 0, 0, 1, 0);
                i++;
            }

            if (axis.Z == 0)
            {
                d.JointSetAMotorAxis(Amotor, i, 0, 0, 0, 1);
                i++;
            }

            /*for (int j = 0; j < (int)axisnum; j++)
            {
                d.JointSetAMotorAngle(Amotor, j, 0);
            }*/

            // These lowstops and high stops are effectively (no wiggle room)
            d.JointSetAMotorParam(Amotor, (int)dParam.LowStop, 0.01f);
            d.JointSetAMotorParam(Amotor, (int)dParam.LoStop3, 0.01f);
            d.JointSetAMotorParam(Amotor, (int)dParam.LoStop2, 0.01f);
            d.JointSetAMotorParam(Amotor, (int)dParam.HiStop, 0.01f);
            d.JointSetAMotorParam(Amotor, (int)dParam.HiStop3, 0.01f);
            d.JointSetAMotorParam(Amotor, (int)dParam.HiStop2, 0.01f);
            d.JointSetAMotorParam(Amotor, (int)dParam.Vel, 9000);
            d.JointSetAMotorParam(Amotor, (int)dParam.FudgeFactor, 0f);
            d.JointSetAMotorParam(Amotor, (int)dParam.FMax, 55000000);
            d.JointAttach(Amotor, Body, IntPtr.Zero);
            d.JointAddAMotorTorques(Amotor, 10000, 10000, 10000);

            AmotorRotation = d.BodyGetRotation(Body);
        }

        public Matrix4 FromDMass(d.Mass pMass)
        {
            Matrix4 obj;
            obj.M11 = pMass.I.M00;
            obj.M12 = pMass.I.M01;
            obj.M13 = pMass.I.M02;
            obj.M14 = 0;
            obj.M21 = pMass.I.M10;
            obj.M22 = pMass.I.M11;
            obj.M23 = pMass.I.M12;
            obj.M24 = 0;
            obj.M31 = pMass.I.M20;
            obj.M32 = pMass.I.M21;
            obj.M33 = pMass.I.M22;
            obj.M34 = 0;
            obj.M41 = 0;
            obj.M42 = 0;
            obj.M43 = 0;
            obj.M44 = 1;
            return obj;
        }

        public d.Mass FromMatrix4(Matrix4 pMat, ref d.Mass obj)
        {
            obj.I.M00 = pMat[0, 0];
            obj.I.M01 = pMat[0, 1];
            obj.I.M02 = pMat[0, 2];
            obj.I.M10 = pMat[1, 0];
            obj.I.M11 = pMat[1, 1];
            obj.I.M12 = pMat[1, 2];
            obj.I.M20 = pMat[2, 0];
            obj.I.M21 = pMat[2, 1];
            obj.I.M22 = pMat[2, 2];
            return obj;
        }

        public override void SubscribeEvents(int ms)
        {
            m_eventsubscription = ms;
            _parent_scene.addCollisionEventReporting(this);
        }

        public override void UnSubscribeEvents()
        {
            _parent_scene.remCollisionEventReporting(this);
            m_eventsubscription = 0;
        }

        public void AddCollisionEvent(uint CollidedWith, ContactPoint contact)
        {
            if (CollisionEventsThisFrame == null)
                CollisionEventsThisFrame = new CollisionEventUpdate();
            CollisionEventsThisFrame.addCollider(CollidedWith, contact);
        }

        public void SendCollisions()
        {
            if (CollisionEventsThisFrame == null)
                return;

            base.SendCollisionUpdate(CollisionEventsThisFrame);

            if (CollisionEventsThisFrame.m_objCollisionList.Count == 0)
                CollisionEventsThisFrame = null;
            else
                CollisionEventsThisFrame = new CollisionEventUpdate();
        }

        public override bool SubscribedEvents()
        {
            if (m_eventsubscription > 0)
                return true;
            return false;
        }

        public static Matrix4 Inverse(Matrix4 pMat)
        {
            if (determinant3x3(pMat) == 0)
            {
                return Matrix4.Identity; // should probably throw an error.  singluar matrix inverse not possible
            }



            return (Adjoint(pMat) / determinant3x3(pMat));
        }

        public static Matrix4 Adjoint(Matrix4 pMat)
        {
            Matrix4 adjointMatrix = new Matrix4();
            for (int i=0; i<4; i++)
            {
                for (int j=0; j<4; j++)
                {
                    Matrix4SetValue(ref adjointMatrix, i, j, (float)(Math.Pow(-1, i + j) * (determinant3x3(Minor(pMat, i, j)))));
                }
            }

            adjointMatrix = Transpose(adjointMatrix);
            return adjointMatrix;
        }

        public static Matrix4 Minor(Matrix4 matrix, int iRow, int iCol)
        {
            Matrix4 minor = new Matrix4();
            int m = 0, n = 0;
            for (int i = 0; i < 4; i++)
            {
                if (i == iRow)
                    continue;
                n = 0;
                for (int j = 0; j < 4; j++)
                {
                    if (j == iCol)
                        continue;
                    Matrix4SetValue(ref minor, m,n, matrix[i, j]);
                    n++;
                }
                m++;
            }
            return minor;
        }

        public static Matrix4 Transpose(Matrix4 pMat)
        {
            Matrix4 transposeMatrix = new Matrix4();
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    Matrix4SetValue(ref transposeMatrix, i, j, pMat[j, i]);
            return transposeMatrix;
        }

        public static void Matrix4SetValue(ref Matrix4 pMat, int r, int c, float val)
        {
            switch (r)
            {
                case 0:
                    switch (c)
                    {
                        case 0:
                            pMat.M11 = val;
                            break;
                        case 1:
                            pMat.M12 = val;
                            break;
                        case 2:
                            pMat.M13 = val;
                            break;
                        case 3:
                            pMat.M14 = val;
                            break;
                    }

                    break;
                case 1:
                    switch (c)
                    {
                        case 0:
                            pMat.M21 = val;
                            break;
                        case 1:
                            pMat.M22 = val;
                            break;
                        case 2:
                            pMat.M23 = val;
                            break;
                        case 3:
                            pMat.M24 = val;
                            break;
                    }

                    break;
                case 2:
                    switch (c)
                    {
                        case 0:
                            pMat.M31 = val;
                            break;
                        case 1:
                            pMat.M32 = val;
                            break;
                        case 2:
                            pMat.M33 = val;
                            break;
                        case 3:
                            pMat.M34 = val;
                            break;
                    }

                    break;
                case 3:
                    switch (c)
                    {
                        case 0:
                            pMat.M41 = val;
                            break;
                        case 1:
                            pMat.M42 = val;
                            break;
                        case 2:
                            pMat.M43 = val;
                            break;
                        case 3:
                            pMat.M44 = val;
                            break;
                    }

                    break;
            }
        }
        private static float determinant3x3(Matrix4 pMat)
        {
            float det = 0;
            float diag1 = pMat[0, 0]*pMat[1, 1]*pMat[2, 2];
            float diag2 = pMat[0, 1]*pMat[2, 1]*pMat[2, 0];
            float diag3 = pMat[0, 2]*pMat[1, 0]*pMat[2, 1];
            float diag4 = pMat[2, 0]*pMat[1, 1]*pMat[0, 2];
            float diag5 = pMat[2, 1]*pMat[1, 2]*pMat[0, 0];
            float diag6 = pMat[2, 2]*pMat[1, 0]*pMat[0, 1];

            det = diag1 + diag2 + diag3 - (diag4 + diag5 + diag6);
            return det;

        }
        
        private static void DMassCopy(ref d.Mass src, ref d.Mass dst)
        {
            dst.c.W = src.c.W;
            dst.c.X = src.c.X;
            dst.c.Y = src.c.Y;
            dst.c.Z = src.c.Z;
            dst.mass = src.mass;
            dst.I.M00 = src.I.M00;
            dst.I.M01 = src.I.M01;
            dst.I.M02 = src.I.M02;
            dst.I.M10 = src.I.M10;
            dst.I.M11 = src.I.M11;
            dst.I.M12 = src.I.M12;
            dst.I.M20 = src.I.M20;
            dst.I.M21 = src.I.M21;
            dst.I.M22 = src.I.M22;
        }

        public override void SetMaterial(int pMaterial)
        {
            m_material = pMaterial;
        }

    }
}
