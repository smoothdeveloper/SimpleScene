﻿using System;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace SimpleScene
{
	public class SSSkeletalRenderMesh : SSIndexedMesh<SSVertex_PosNormTex>
	{
		// TODO Textures per render mesh, extracted from SSSkeletalMesh
		// TODO Detach mesh

		public float TimeScale = 1f;

		protected readonly SSSkeletalMeshRuntime m_skeletalMesh;
		protected readonly SSVertex_PosNormTex[] m_vertices;
		protected Dictionary<int, SSSkeletalAnimationChannel> m_animChannels
			= new Dictionary<int, SSSkeletalAnimationChannel>();
		protected SSSphere m_boundingSphere;
		protected List<SSSkeletalRenderMesh> m_attachedSkeletalMeshes;

		public override bool alphaBlendingEnabled {
			get { return base.alphaBlendingEnabled; }
			set {
				base.alphaBlendingEnabled = value;
				if (m_attachedSkeletalMeshes != null) {
					foreach (var child in m_attachedSkeletalMeshes) {
						child.alphaBlendingEnabled = value;
					}
				}
			}
		}

		public SSSkeletalRenderMesh (SSSkeletalMesh skeletalMesh, 
									 SSSkeletalJointRuntime[] sharedJoints = null)
			: base(null, skeletalMesh.TriangleIndices)
		{
			m_skeletalMesh = new SSSkeletalMeshRuntime(skeletalMesh, sharedJoints);
			m_vertices = new SSVertex_PosNormTex[m_skeletalMesh.NumVertices];
			for (int v = 0; v < m_skeletalMesh.NumVertices; ++v) {
				m_vertices [v].TexCoord = m_skeletalMesh.TextureCoords (v);
                m_vertices [v].Normal = m_skeletalMesh.BindPoseNormal(v);
			}
			m_attachedSkeletalMeshes = new List<SSSkeletalRenderMesh> ();
			computeVertices ();

			string matString = skeletalMesh.MaterialShaderString;
			if (matString != null && matString.Length > 0) {
				base.textureMaterial 
					= SSTextureMaterial.FromMaterialString (skeletalMesh.AssetContext, matString);
			}
		}

		public SSSkeletalRenderMesh(SSSkeletalMesh[] skeletalMeshes) 
			: this(skeletalMeshes[0], null)
		{
			for (int i = 1; i < skeletalMeshes.Length; ++i) {
				AttachMesh (skeletalMeshes [i]);
			}
		}

		public void AddChannel(int channelId, params int[] topLevelActiveJointIds)
		{
			foreach (int cid in topLevelActiveJointIds) {
				if (cid == -1) { // means include all
					topLevelActiveJointIds = m_skeletalMesh.TopLevelJoints;
					break;
				}
			}
			var channel = new SSSkeletalAnimationChannel (topLevelActiveJointIds);
			m_animChannels.Add (channelId, channel);
		}

		public void AddChannel(int channelId, params string[] topLevelActiveJointNames)
		{
			int[] topLevelActiveJointsIds = new int[topLevelActiveJointNames.Length];
			for (int n = 0; n < topLevelActiveJointsIds.Length; ++n) {
				topLevelActiveJointsIds [n] = m_skeletalMesh.JointIndex (topLevelActiveJointNames [n]);
			}
			AddChannel (channelId, topLevelActiveJointsIds);
		}

		public void PlayAnimation(int channelId, SSSkeletalAnimation anim,
								  bool repeat, float transitionTime)
		{
			m_skeletalMesh.VerifyAnimation (anim);
			var channel = m_animChannels [channelId];
			channel.PlayAnimation (anim, repeat, transitionTime);
		}

		public void AttachMesh(SSSkeletalMesh mesh)
		{
			var newRender = new SSSkeletalRenderMesh (mesh, m_skeletalMesh.Joints);
			newRender.alphaBlendingEnabled = this.alphaBlendingEnabled;
			m_attachedSkeletalMeshes.Add (newRender);
		}

		public override void RenderMesh(ref SSRenderConfig renderConfig)
		{
			// apply animation channels
			var channels = new List<SSSkeletalAnimationChannel> ();
			channels.AddRange (m_animChannels.Values);
			m_skeletalMesh.ApplyAnimationChannels (channels);

			// compute vertex positions, normals
			computeVertices ();

			// render vertices + indices
			base.RenderMesh (ref renderConfig);
			foreach (SSSkeletalRenderMesh rm in m_attachedSkeletalMeshes) {
				rm.RenderMesh (ref renderConfig);
			}

            
            // debugging vertex normals... 
			#if false
            {
                // do not change the order!!
                renderFaceNormals();               // these are correct..
                renderFaceAveragedVertexNormals(); // these are correct..                
                // renderBindPoseVertexNormals ();
                renderAnimatedVertexNormals();     // these are currently WRONG
            }
			#endif

			#if false
			SSShaderProgram.DeactivateAll ();
			// bounding box debugging
			GL.PolygonMode(MaterialFace.Front, PolygonMode.Line);
			GL.Disable (EnableCap.Texture2D);
			GL.Translate (aabb.Center ());
			GL.Scale (aabb.Diff ());
			GL.Color4 (1f, 0f, 0f, 0.1f);
			SSTexturedCube.Instance.DrawArrays (ref renderConfig, PrimitiveType.Triangles);
			GL.PolygonMode(MaterialFace.Front, PolygonMode.Fill);
			#endif
		}

		public override void Update(float elapsedS)
		{
			elapsedS *= TimeScale;
			foreach (var channel in m_animChannels.Values) {
				channel.Update (elapsedS);
			}
		}

		public override bool TraverseTriangles<T>(T state, traverseFn<T> fn) {
			for(int idx=0; idx < m_skeletalMesh.Indices.Length; idx+=3) {
				var v1 = m_skeletalMesh.ComputeVertexPos (m_skeletalMesh.Indices[idx]);
				var v2 = m_skeletalMesh.ComputeVertexPos (m_skeletalMesh.Indices[idx+1]);
				var v3 = m_skeletalMesh.ComputeVertexPos (m_skeletalMesh.Indices[idx+2]);
				bool finished = fn(state, v1, v2, v3);
				if (finished) { 
					return true;
				}
			}
			foreach (SSSkeletalRenderMesh a in m_attachedSkeletalMeshes) {
				bool finished = a.TraverseTriangles (state, fn);
				if (finished) {
					return true;
				}
			}
			return false;
		}

		private void computeVertices()
		{
			SSAABB aabb= new SSAABB (float.PositiveInfinity, float.NegativeInfinity);
			for (int v = 0; v < m_skeletalMesh.NumVertices; ++v) {
				// position
				Vector3 pos = m_skeletalMesh.ComputeVertexPos (v);
				m_vertices [v].Position = pos;
				aabb.UpdateMin (pos);
				aabb.UpdateMax (pos);
				// normal
				m_vertices [v].Normal = m_skeletalMesh.ComputeVertexNormal (v);
			}
			m_vbo.UpdateBufferData (m_vertices);
			m_boundingSphere = aabb.ToSphere ();
			MeshChanged ();
		}


        private void renderFaceNormals() 
        {
            SSShaderProgram.DeactivateAll();
            GL.Color4(Color4.Green);
            for (int i=0;i<m_skeletalMesh.NumTriangles;i++) {
                int baseIdx = i * 3;                
                Vector3 p0 = m_skeletalMesh.ComputeVertexPosFromTriIndex(baseIdx);
                Vector3 p1 = m_skeletalMesh.ComputeVertexPosFromTriIndex(baseIdx + 1);
                Vector3 p2 = m_skeletalMesh.ComputeVertexPosFromTriIndex(baseIdx + 2);

                Vector3 face_center = (p0 + p1 + p2) / 3.0f;
                Vector3 face_normal = Vector3.Cross(p1 - p0, p2 - p0).Normalized();

                GL.Begin(PrimitiveType.Lines);
                GL.Vertex3(face_center);
                GL.Vertex3(face_center + face_normal * 0.2f);
                GL.End();                
            }
        }

        private void renderAnimatedVertexNormals() {
            SSShaderProgram.DeactivateAll();
            GL.Color4(Color4.Magenta);
            for (int v = 0; v < m_vertices.Length; ++v) {                

                GL.Begin(PrimitiveType.Lines);
                GL.Vertex3(m_vertices[v].Position);
                GL.Vertex3(m_vertices[v].Position + m_vertices[v].Normal * 0.2f);
                GL.End();
            }
        }

		private void renderBindPoseVertexNormals()
		{
			SSShaderProgram.DeactivateAll ();
			GL.Color4 (Color4.White);
			for (int v = 0; v < m_vertices.Length; ++v) {                
				GL.Begin (PrimitiveType.Lines);
				GL.Vertex3 (m_vertices [v].Position);
				GL.Vertex3 (m_vertices [v].Position + m_skeletalMesh.BindPoseNormal(v) * 0.3f); 
				GL.End ();
			}
		}

        public void renderFaceAveragedVertexNormals() {
            Vector3[] perVertexNormals = new Vector3[m_skeletalMesh.NumVertices];

            for (int i = 0; i < m_skeletalMesh.NumTriangles; i++) {
                int baseIdx = i * 3;
                Vector3 p0 = m_skeletalMesh.ComputeVertexPosFromTriIndex(baseIdx);
                Vector3 p1 = m_skeletalMesh.ComputeVertexPosFromTriIndex(baseIdx + 1);
                Vector3 p2 = m_skeletalMesh.ComputeVertexPosFromTriIndex(baseIdx + 2);
                
                Vector3 face_normal = Vector3.Cross(p1 - p0, p2 - p0).Normalized();

                int v0 = m_skeletalMesh.Indices[baseIdx];
				int v1 = m_skeletalMesh.Indices[baseIdx + 1];
				int v2 = m_skeletalMesh.Indices[baseIdx + 2];

                perVertexNormals[v0] += face_normal;
                perVertexNormals[v1] += face_normal;
                perVertexNormals[v2] += face_normal;

            }

            // render face averaged vertex normals

            SSShaderProgram.DeactivateAll();
            GL.Color4(Color4.Yellow);
            for (int v=0;v<perVertexNormals.Length;v++) {
                GL.Begin(PrimitiveType.Lines);
                GL.Vertex3(m_skeletalMesh.ComputeVertexPos(v));
                GL.Vertex3(m_skeletalMesh.ComputeVertexPos(v) + perVertexNormals[v].Normalized() * 0.5f);
                GL.End();
            }
        }

		public override Vector3 Center ()
		{
			return m_boundingSphere.center;
		}

		public override float Radius ()
		{
			return m_boundingSphere.radius;
		}
	}
}

