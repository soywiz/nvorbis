﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NVorbis.Vorbis
{
	class Mapping0 : FuncMapping
	{
		//static int seq = 0;

		override internal void free_info(Object imap)
		{
		}

		override internal void free_look(Object imap)
		{
		}

		override internal Object look(DspState vd, InfoMode vm, Object m)
		{
			//System.err.println("Mapping0.look");
			Info vi = vd.vi;
			LookMapping0 look = new LookMapping0();
			InfoMapping0 info = look.map = (InfoMapping0)m;
			look.mode = vm;

			look.time_look = new Object[info.submaps];
			look.floor_look = new Object[info.submaps];
			look.residue_look = new Object[info.submaps];

			look.time_func = new FuncTime[info.submaps];
			look.floor_func = new FuncFloor[info.submaps];
			look.residue_func = new FuncResidue[info.submaps];

			for (int i = 0; i < info.submaps; i++)
			{
				int timenum = info.timesubmap[i];
				int floornum = info.floorsubmap[i];
				int resnum = info.residuesubmap[i];

				look.time_func[i] = FuncTime.time_P[vi.TimeType[timenum]];
				look.time_look[i] = look.time_func[i].look(vd, vm, vi.TimeParam[timenum]);
				look.floor_func[i] = FuncFloor.floor_P[vi.FloorType[floornum]];
				look.floor_look[i] = look.floor_func[i].look(vd, vm, vi.FloorParam[floornum]);
				look.residue_func[i] = FuncResidue.residue_P[vi.residue_type[resnum]];
				look.residue_look[i] = look.residue_func[i].look(vd, vm, vi.ResidueParam[resnum]);

			}

			/*
			if (vi.psys != 0 && vd.analysisp != 0)
			{
				// ??
			}
			*/

			look.ch = vi.Channels;

			return (look);
		}

		override internal void pack(Info vi, Object imap, NVorbis.Ogg.BBuffer opb)
		{
			InfoMapping0 info = (InfoMapping0)imap;

			/* another 'we meant to do it this way' hack...  up to beta 4, we
			   packed 4 binary zeros here to signify one submapping in use.  We
			   now redefine that to mean four bitflags that indicate use of
			   deeper features; bit0:submappings, bit1:coupling,
			   bit2,3:reserved. This is backward compatable with all actual uses
			   of the beta code. */

			if (info.submaps > 1)
			{
				opb.Write(1, 1);
				opb.Write(info.submaps - 1, 4);
			}
			else
			{
				opb.Write(0, 1);
			}

			if (info.coupling_steps > 0)
			{
				opb.Write(1, 1);
				opb.Write(info.coupling_steps - 1, 8);
				for (int i = 0; i < info.coupling_steps; i++)
				{
					opb.Write(info.coupling_mag[i], Util.ilog2(vi.Channels));
					opb.Write(info.coupling_ang[i], Util.ilog2(vi.Channels));
				}
			}
			else
			{
				opb.Write(0, 1);
			}

			opb.Write(0, 2); /* 2,3:reserved */

			/* we don't write the channel submappings if we only have one... */
			if (info.submaps > 1)
			{
				for (int i = 0; i < vi.Channels; i++)
					opb.Write(info.chmuxlist[i], 4);
			}
			for (int i = 0; i < info.submaps; i++)
			{
				opb.Write(info.timesubmap[i], 8);
				opb.Write(info.floorsubmap[i], 8);
				opb.Write(info.residuesubmap[i], 8);
			}
		}

		// also responsible for range checking
		override internal Object unpack(Info vi, NVorbis.Ogg.BBuffer opb)
		{
			InfoMapping0 info = new InfoMapping0();

			if (opb.Read(1) != 0)
			{
				info.submaps = opb.Read(4) + 1;
			}
			else
			{
				info.submaps = 1;
			}

			if (opb.Read(1) != 0)
			{
				info.coupling_steps = opb.Read(8) + 1;

				for (int i = 0; i < info.coupling_steps; i++)
				{
					int testM = info.coupling_mag[i] = opb.Read(Util.ilog2(vi.Channels));
					int testA = info.coupling_ang[i] = opb.Read(Util.ilog2(vi.Channels));

					if (testM < 0 || testA < 0 || testM == testA || testM >= vi.Channels
						|| testA >= vi.Channels)
					{
						//goto err_out;
						info.free();
						return (null);
					}
				}
			}

			if (opb.Read(2) > 0)
			{ /* 2,3:reserved */
				info.free();
				return (null);
			}

			if (info.submaps > 1)
			{
				for (int i = 0; i < vi.Channels; i++)
				{
					info.chmuxlist[i] = opb.Read(4);
					if (info.chmuxlist[i] >= info.submaps)
					{
						info.free();
						return (null);
					}
				}
			}

			for (int i = 0; i < info.submaps; i++)
			{
				info.timesubmap[i] = opb.Read(8);
				if (info.timesubmap[i] >= vi.Times)
				{
					info.free();
					return (null);
				}
				info.floorsubmap[i] = opb.Read(8);
				if (info.floorsubmap[i] >= vi.floors)
				{
					info.free();
					return (null);
				}
				info.residuesubmap[i] = opb.Read(8);
				if (info.residuesubmap[i] >= vi.residues)
				{
					info.free();
					return (null);
				}
			}
			return info;
		}

		internal float[][] pcmbundle = null;
		internal int[] zerobundle = null;
		internal int[] nonzero = null;
		internal Object[] floormemo = null;

		override internal int inverse(Block vb, Object l)
		{
			lock (this)
			{
				DspState vd = vb.vd;
				Info vi = vd.vi;
				LookMapping0 look = (LookMapping0)l;
				InfoMapping0 info = look.map;
				InfoMode mode = look.mode;
				int n = vb.pcmend = vi.blocksizes[vb.W];

				float[] window = vd._window[vb.W][vb.lW][vb.nW][mode.windowtype];
				if (pcmbundle == null || pcmbundle.Length < vi.Channels)
				{
					pcmbundle = new float[vi.Channels][];
					nonzero = new int[vi.Channels];
					zerobundle = new int[vi.Channels];
					floormemo = new Object[vi.Channels];
				}

				// time domain information decode (note that applying the
				// information would have to happen later; we'll probably add a
				// function entry to the harness for that later
				// NOT IMPLEMENTED

				// recover the spectral envelope; store it in the PCM vector for now 
				for (int i = 0; i < vi.Channels; i++)
				{
					float[] pcm = vb.pcm[i];
					int submap = info.chmuxlist[i];

					floormemo[i] = look.floor_func[submap].inverse1(vb,
						look.floor_look[submap], floormemo[i]);
					if (floormemo[i] != null)
					{
						nonzero[i] = 1;
					}
					else
					{
						nonzero[i] = 0;
					}
					for (int j = 0; j < n / 2; j++)
					{
						pcm[j] = 0;
					}

				}

				for (int i = 0; i < info.coupling_steps; i++)
				{
					if (nonzero[info.coupling_mag[i]] != 0 || nonzero[info.coupling_ang[i]] != 0)
					{
						nonzero[info.coupling_mag[i]] = 1;
						nonzero[info.coupling_ang[i]] = 1;
					}
				}

				// recover the residue, apply directly to the spectral envelope

				for (int i = 0; i < info.submaps; i++)
				{
					int ch_in_bundle = 0;
					for (int j = 0; j < vi.Channels; j++)
					{
						if (info.chmuxlist[j] == i)
						{
							if (nonzero[j] != 0)
							{
								zerobundle[ch_in_bundle] = 1;
							}
							else
							{
								zerobundle[ch_in_bundle] = 0;
							}
							pcmbundle[ch_in_bundle++] = vb.pcm[j];
						}
					}

					look.residue_func[i].inverse(vb, look.residue_look[i], pcmbundle,
						zerobundle, ch_in_bundle);
				}

				for (int i = info.coupling_steps - 1; i >= 0; i--)
				{
					float[] pcmM = vb.pcm[info.coupling_mag[i]];
					float[] pcmA = vb.pcm[info.coupling_ang[i]];

					for (int j = 0; j < n / 2; j++)
					{
						float mag = pcmM[j];
						float ang = pcmA[j];

						if (mag > 0)
						{
							if (ang > 0)
							{
								pcmM[j] = mag;
								pcmA[j] = mag - ang;
							}
							else
							{
								pcmA[j] = mag;
								pcmM[j] = mag + ang;
							}
						}
						else
						{
							if (ang > 0)
							{
								pcmM[j] = mag;
								pcmA[j] = mag + ang;
							}
							else
							{
								pcmA[j] = mag;
								pcmM[j] = mag - ang;
							}
						}
					}
				}

				//    /* compute and apply spectral envelope */

				for (int i = 0; i < vi.Channels; i++)
				{
					float[] pcm = vb.pcm[i];
					int submap = info.chmuxlist[i];
					look.floor_func[submap].inverse2(vb, look.floor_look[submap],
						floormemo[i], pcm);
				}

				// transform the PCM data; takes PCM vector, vb; modifies PCM vector
				// only MDCT right now....

				for (int i = 0; i < vi.Channels; i++)
				{
					float[] pcm = vb.pcm[i];
					//_analysis_output("out",seq+i,pcm,n/2,0,0);
					((Mdct)vd.transform[vb.W][0]).Backward(pcm, pcm);
				}

				// now apply the decoded pre-window time information
				// NOT IMPLEMENTED

				// window the data
				for (int i = 0; i < vi.Channels; i++)
				{
					float[] pcm = vb.pcm[i];
					if (nonzero[i] != 0)
					{
						for (int j = 0; j < n; j++)
						{
							pcm[j] *= window[j];
						}
					}
					else
					{
						for (int j = 0; j < n; j++)
						{
							pcm[j] = 0.0f;
						}
					}
				}

				// now apply the decoded post-window time information
				// NOT IMPLEMENTED
				// all done!
				return (0);
			}
		}

		internal class InfoMapping0
		{
			internal int submaps; // <= 16
			internal int[] chmuxlist = new int[256]; // up to 256 channels in a Vorbis stream

			internal int[] timesubmap = new int[16]; // [mux]
			internal int[] floorsubmap = new int[16]; // [mux] submap to floors
			internal int[] residuesubmap = new int[16];// [mux] submap to residue
			internal int[] psysubmap = new int[16]; // [mux]; encode only

			internal int coupling_steps;
			internal int[] coupling_mag = new int[256];
			internal int[] coupling_ang = new int[256];

			internal void free()
			{
				chmuxlist = null;
				timesubmap = null;
				floorsubmap = null;
				residuesubmap = null;
				psysubmap = null;

				coupling_mag = null;
				coupling_ang = null;
			}
		}

		internal class LookMapping0
		{
			internal InfoMode mode;
			internal InfoMapping0 map;
			internal Object[] time_look;
			internal Object[] floor_look;
			//internal Object[] floor_state;

			internal Object[] residue_look;
			//internal PsyLook[] psy_look;

			internal FuncTime[] time_func;
			internal FuncFloor[] floor_func;
			internal FuncResidue[] residue_func;

			internal int ch;
			//internal float[][] decay;
			//internal int lastframe; // if a different mode is called, we need to 
			// invalidate decay and floor state
		}

	}

}
