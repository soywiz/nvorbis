﻿/* -*-mode:java; c-basic-offset:2; indent-tabs-mode:nil -*- */
/* JOrbis
 * Copyright (C) 2000 ymnk, JCraft,Inc.
 *  
 * Written by: 2000 ymnk<ymnk@jcraft.com>
 *   
 * Many thanks to 
 *   Monty <monty@xiph.org> and 
 *   The XIPHOPHORUS Company http://www.xiph.org/ .
 * JOrbis has been based on their awesome works, Vorbis codec.
 *   
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Library General Public License
 * as published by the Free Software Foundation; either version 2 of
 * the License, or (at your option) any later version.
   
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Library General Public License for more details.
 * 
 * You should have received a copy of the GNU Library General Public
 * License along with this program; if not, write to the Free Software
 * Foundation, Inc., 675 Mass Ave, Cambridge, MA 02139, USA.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NVorbis.Vorbis
{
	public class DspState
	{
		internal const float M_PI = 3.1415926539f;
		internal const int VI_TRANSFORMB = 1;
		internal const int VI_WINDOWB = 1;

		//internal int analysisp;
		internal Info vi;
		internal int modebits;

		internal float[][] pcm;
		internal int pcm_storage;
		internal int pcm_current;
		internal int pcm_returned;

		//internal float[] multipliers;

		//internal int envelope_storage;
		//internal int envelope_current;

		internal int eofflag;

		internal int lW;
		internal int W;
		internal int nW;
		internal int centerW;

		internal long granulepos;
		internal long sequence;

		internal long glue_bits;
		internal long time_bits;
		internal long floor_bits;
		internal long res_bits;

		// local lookup storage
		internal float[][][][][] _window; // block, leadin, leadout, type
		internal Object[][] transform;
		internal CodeBook[] fullbooks;
		// backend lookups are tied to the mode, not the backend or naked mapping
		internal Object[] mode;

		// local storage, only used on the encoding side.  This way the
		// application does not need to worry about freeing some packets'
		// memory and not others'; packet storage is always tracked.
		// Cleared next call to a _dsp_ function
		/*
		internal byte[] header;
		internal byte[] header1;
		internal byte[] header2;
		*/

		public DspState()
		{
			transform = new Object[2][];
			_window = new float[2][][][][];
			_window[0] = new float[2][][][];
			_window[0][0] = new float[2][][];
			_window[0][1] = new float[2][][];
			_window[0][0][0] = new float[2][];
			_window[0][0][1] = new float[2][];
			_window[0][1][0] = new float[2][];
			_window[0][1][1] = new float[2][];
			_window[1] = new float[2][][][];
			_window[1][0] = new float[2][][];
			_window[1][1] = new float[2][][];
			_window[1][0][0] = new float[2][];
			_window[1][0][1] = new float[2][];
			_window[1][1][0] = new float[2][];
			_window[1][1][1] = new float[2][];
		}

		static float[] window(int type, int window, int left, int right)
		{
			float[] ret = new float[window];
			switch (type)
			{
				case 0:
					// The 'vorbis window' (window 0) is sin(sin(x)*sin(x)*2pi)
					{
						int leftbegin = window / 4 - left / 2;
						int rightbegin = window - window / 4 - right / 2;

						for (int i = 0; i < left; i++)
						{
							float x = (float)((i + .5) / left * M_PI / 2.0f);
							x = (float)Math.Sin(x);
							x *= x;
							x *= M_PI / 2.0f;
							x = (float)Math.Sin(x);
							ret[i + leftbegin] = x;
						}

						for (int i = leftbegin + left; i < rightbegin; i++)
						{
							ret[i] = 1.0f;
						}

						for (int i = 0; i < right; i++)
						{
							float x = (float)((right - i - .5) / right * M_PI / 2.0f);
							x = (float)Math.Sin(x);
							x *= x;
							x *= M_PI / 2.0f;
							x = (float)Math.Sin(x);
							ret[i + rightbegin] = x;
						}
					}
					break;
				default:
					//free(ret);
					return (null);
			}
			return (ret);
		}

		// Analysis side code, but directly related to blocking.  Thus it's
		// here and not in analysis.c (which is for analysis transforms only).
		// The init is here because some of it is shared

		int init(Info vi, bool encp)
		{
			this.vi = vi;
			modebits = Util.ilog2(vi.Modes);

			transform[0] = new Object[VI_TRANSFORMB];
			transform[1] = new Object[VI_TRANSFORMB];

			// MDCT is tranform 0

			transform[0][0] = new Mdct();
			transform[1][0] = new Mdct();
			((Mdct)transform[0][0]).Init(vi.blocksizes[0]);
			((Mdct)transform[1][0]).Init(vi.blocksizes[1]);

			_window[0][0][0] = new float[VI_WINDOWB][];
			_window[0][0][1] = _window[0][0][0];
			_window[0][1][0] = _window[0][0][0];
			_window[0][1][1] = _window[0][0][0];
			_window[1][0][0] = new float[VI_WINDOWB][];
			_window[1][0][1] = new float[VI_WINDOWB][];
			_window[1][1][0] = new float[VI_WINDOWB][];
			_window[1][1][1] = new float[VI_WINDOWB][];

			for (int i = 0; i < VI_WINDOWB; i++)
			{
				_window[0][0][0][i] = window(i, vi.blocksizes[0], vi.blocksizes[0] / 2,
					vi.blocksizes[0] / 2);
				_window[1][0][0][i] = window(i, vi.blocksizes[1], vi.blocksizes[0] / 2,
					vi.blocksizes[0] / 2);
				_window[1][0][1][i] = window(i, vi.blocksizes[1], vi.blocksizes[0] / 2,
					vi.blocksizes[1] / 2);
				_window[1][1][0][i] = window(i, vi.blocksizes[1], vi.blocksizes[1] / 2,
					vi.blocksizes[0] / 2);
				_window[1][1][1][i] = window(i, vi.blocksizes[1], vi.blocksizes[1] / 2,
					vi.blocksizes[1] / 2);
			}

			fullbooks = new CodeBook[vi.Books];
			for (int i = 0; i < vi.Books; i++)
			{
				fullbooks[i] = new CodeBook();
				fullbooks[i].init_decode(vi.BookParam[i]);
			}

			// initialize the storage vectors to a decent size greater than the
			// minimum

			pcm_storage = 8192; // we'll assume later that we have
			// a minimum of twice the blocksize of
			// accumulated samples in analysis
			pcm = new float[vi.Channels][];
			{
				for (int i = 0; i < vi.Channels; i++)
				{
					pcm[i] = new float[pcm_storage];
				}
			}

			// all 1 (large block) or 0 (small block)
			// explicitly set for the sake of clarity
			lW = 0; // previous window size
			W = 0; // current window size

			// all vector indexes; multiples of samples_per_envelope_step
			centerW = vi.blocksizes[1] / 2;

			pcm_current = centerW;

			// initialize all the mapping/backend lookups
			mode = new Object[vi.Modes];
			for (int i = 0; i < vi.Modes; i++)
			{
				int mapnum = vi.ModeParam[i].mapping;
				int maptype = vi.map_type[mapnum];
				mode[i] = FuncMapping.mapping_P[maptype].look(this, vi.ModeParam[i],
					vi.MapParam[mapnum]);
			}
			return (0);
		}

		public int synthesis_init(Info vi)
		{
			init(vi, false);
			// Adjust centerW to allow an easier mechanism for determining output
			pcm_returned = centerW;
			centerW -= vi.blocksizes[W] / 4 + vi.blocksizes[lW] / 4;
			granulepos = -1;
			sequence = -1;
			return (0);
		}

		DspState(Info vi)
		{
			//this();
			init(vi, false);
			// Adjust centerW to allow an easier mechanism for determining output
			pcm_returned = centerW;
			centerW -= vi.blocksizes[W] / 4 + vi.blocksizes[lW] / 4;
			granulepos = -1;
			sequence = -1;
		}

		// Unike in analysis, the window is only partially applied for each
		// block.  The time domain envelope is not yet handled at the point of
		// calling (as it relies on the previous block).

		public int synthesis_blockin(Block vb)
		{
			// Shift out any PCM/multipliers that we returned previously
			// centerW is currently the center of the last block added
			if (centerW > vi.blocksizes[1] / 2 && pcm_returned > 8192)
			{
				// don't shift too much; we need to have a minimum PCM buffer of
				// 1/2 long block

				int shiftPCM = centerW - vi.blocksizes[1] / 2;
				shiftPCM = (pcm_returned < shiftPCM ? pcm_returned : shiftPCM);

				pcm_current -= shiftPCM;
				centerW -= shiftPCM;
				pcm_returned -= shiftPCM;
				if (shiftPCM != 0)
				{
					for (int i = 0; i < vi.Channels; i++)
					{
						Array.Copy(pcm[i], shiftPCM, pcm[i], 0, pcm_current);
					}
				}
			}

			lW = W;
			W = vb.W;
			nW = -1;

			glue_bits += vb.glue_bits;
			time_bits += vb.time_bits;
			floor_bits += vb.floor_bits;
			res_bits += vb.res_bits;

			if (sequence + 1 != vb.sequence)
				granulepos = -1; // out of sequence; lose count

			sequence = vb.sequence;

			{
				int sizeW = vi.blocksizes[W];
				int _centerW = centerW + vi.blocksizes[lW] / 4 + sizeW / 4;
				int beginW = _centerW - sizeW / 2;
				int endW = beginW + sizeW;
				int beginSl = 0;
				int endSl = 0;

				// Do we have enough PCM/mult storage for the block?
				if (endW > pcm_storage)
				{
					// expand the storage
					pcm_storage = endW + vi.blocksizes[1];
					for (int i = 0; i < vi.Channels; i++)
					{
						float[] foo = new float[pcm_storage];
						Array.Copy(pcm[i], 0, foo, 0, pcm[i].Length);
						pcm[i] = foo;
					}
				}

				// overlap/add PCM
				switch (W)
				{
					case 0:
						beginSl = 0;
						endSl = vi.blocksizes[0] / 2;
						break;
					case 1:
						beginSl = vi.blocksizes[1] / 4 - vi.blocksizes[lW] / 4;
						endSl = beginSl + vi.blocksizes[lW] / 2;
						break;
				}

				for (int j = 0; j < vi.Channels; j++)
				{
					int _pcm = beginW;
					// the overlap/add section
					int i = 0;
					for (i = beginSl; i < endSl; i++)
					{
						pcm[j][_pcm + i] += vb.pcm[j][i];
					}
					// the remaining section
					for (; i < sizeW; i++)
					{
						pcm[j][_pcm + i] = vb.pcm[j][i];
					}
				}

				// track the frame number... This is for convenience, but also
				// making sure our last packet doesn't end with added padding.  If
				// the last packet is partial, the number of samples we'll have to
				// return will be past the vb->granulepos.
				//       
				// This is not foolproof!  It will be confused if we begin
				// decoding at the last page after a seek or hole.  In that case,
				// we don't have a starting point to judge where the last frame
				// is.  For this reason, vorbisfile will always try to make sure
				// it reads the last two marked pages in proper sequence

				if (granulepos == -1)
				{
					granulepos = vb.granulepos;
				}
				else
				{
					granulepos += (_centerW - centerW);
					if (vb.granulepos != -1 && granulepos != vb.granulepos)
					{
						if (granulepos > vb.granulepos && vb.eofflag != 0)
						{
							// partial last frame.  Strip the padding off
							_centerW -= (int)(granulepos - vb.granulepos);
						}// else{ Shouldn't happen *unless* the bitstream is out of
						// spec.  Either way, believe the bitstream }
						granulepos = vb.granulepos;
					}
				}

				// Update, cleanup

				centerW = _centerW;
				pcm_current = endW;
				if (vb.eofflag != 0)
					eofflag = 1;
			}
			return (0);
		}

		// pcm==NULL indicates we just want the pending samples, no more
		public int synthesis_pcmout(float[][][] _pcm, int[] index)
		{
			if (pcm_returned < centerW)
			{
				if (_pcm != null)
				{
					for (int i = 0; i < vi.Channels; i++)
					{
						index[i] = pcm_returned;
					}
					_pcm[0] = pcm;
				}
				return (centerW - pcm_returned);
			}
			return (0);
		}

		public int synthesis_read(int bytes)
		{
			if (bytes != 0 && pcm_returned + bytes > centerW)
				return (-1);
			pcm_returned += bytes;
			return (0);
		}

		public void clear()
		{
		}
	}

}
